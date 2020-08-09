﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using ENet;
using UnityEngine;
using BitStream = BitStreams.BitStream;

public class DirtyStateTransport : MonoBehaviour
{
    public static readonly float InputSendRate = 10;
    public static readonly float StateSendRate = 5;

    // Happens right before the input packet is created then sent, ushort is the latestPacketReceived
    public static event Action<ushort> PreClientInputSend;
    public static event Action<ushort> PostClientInputSend;
    // Triggers when the client receives a state from the server, to validate client side prediction
    public static event Action<ushort, Vector3> ServerPositionReceived; 

    public Server Server { get; private set; }
    public  Client Client { get; private set; }

    public static float LerpT = 0;
    
    private static ushort currentPacketId = 1;
    private static ushort latestPacketReceived = 0;

    private float _timer = 0;
    
    //Temp?
    public GameObject PlayerPrefab;
    
    private Dictionary<ushort, GameObject> NetworkedObjects = new Dictionary<ushort, GameObject>();
    
    // SERVER ONLY
    // Keeps track of the last input packet that each client sent
    private Dictionary<ushort, ClientInputRecord> ClientInputHistory = new Dictionary<ushort, ClientInputRecord>(); 
    
    // CLIENT ONLY
    private ushort _clientOwnedId;
    private GameObject _clientOwnedObject;
    private bool _clientInitialized;

    public void StartServer()
    {
        Server = new Server();
        Server.PeerConnected += ServerOnPeerConnected;
        Server.PeerDisconncted += ServerOnPeerDisconncted;
        Server.PacketReceived += ServerOnPacketReceived;
        
        Server.Listen(Constants.DefaultPort);
    }

    private void ServerOnPacketReceived(byte[] data, uint senderId)
    {
        // How many frames worth of movement to apply to the entity
        // If the packet is old, none, if this is the next packet, 1
        // If we detect there was a gap in packets, double, etc
        int multiplier = 0;
        
        InputPacket inputPacket = InputPacket.Deserialize(data);
        InputCompressor.Inputs inputs = InputCompressor.DecompressInput(inputPacket.inputByte);
        
        // Check it against the client input history
        if (ClientInputHistory.ContainsKey((ushort) senderId))
        {
            // Check to see if this input is newer than the newest one we know of
            if (inputPacket.id > ClientInputHistory[(ushort) senderId].packetId)
            {
                multiplier = inputPacket.id - ClientInputHistory[(ushort) senderId].packetId;
                
                // Put the record into the dictionary
                ClientInputRecord inputRecord;
                inputRecord.packetId = inputPacket.id;
                inputRecord.inputs = inputs;
                ClientInputHistory[(ushort) senderId] = inputRecord;
            }
            else
            {
                multiplier = 0;
                Debug.Log("Server received out of order client input packet");
            }
        }
        else
        {
            multiplier = 1;
            
            // Put the record into the dictionary
            ClientInputRecord inputRecord;
            inputRecord.packetId = inputPacket.id;
            inputRecord.inputs = inputs;
            ClientInputHistory[(ushort) senderId] = inputRecord;
        }
        
        // GAME LOGIC BEGIN, MOVE TO NEW AREA LATER
        int horizontal = Convert.ToSByte(inputs.D) - Convert.ToSByte(inputs.A);
        int vertical = Convert.ToSByte(inputs.W) - Convert.ToSByte(inputs.S);
        // update state
        NetworkEntity2 entity = NetworkState.LatestEntityDict[(ushort)senderId];
        entity.Position += new Vector3(horizontal, 0, vertical) * multiplier * (1 / InputSendRate);
    }

    private void ServerOnPeerDisconncted(uint id)
    {
        StatePacket disconnectPacket = new StatePacket();
        disconnectPacket.packetType = StatePacket.PacketType.Disconnect;
        disconnectPacket.id = (ushort)id;
        Server.BroadcastBytes(disconnectPacket.Serialize());
        
        // Destroy the networked object
        Destroy(NetworkedObjects[(ushort)id]);
        NetworkedObjects.Remove((ushort) id);
        // Unregister from client input history
        ClientInputHistory.Remove((ushort) id);
        
        NetworkState.LatestEntityDict.Remove((ushort)id);
    }

    private void ServerOnPeerConnected(uint id)
    {
        GameObject obj = Instantiate(PlayerPrefab);
        obj.GetComponent<FollowState>().Id = (ushort)id;
        NetworkedObjects[(ushort) id] = obj;

        NetworkEntity2 entity = new NetworkEntity2()
        {
            id = (ushort) id,
            Position = obj.transform.position,
            Rotation = obj.transform.rotation
        };
        NetworkState.LatestEntityDict[(ushort)id] = entity;

        StatePacket connectPacket = new StatePacket();
        connectPacket.packetType = StatePacket.PacketType.Connect;
        connectPacket.id = (ushort)id;
        // Broadcast the connection to all peers except for the peer that is connecting
        // They will receive their Instantiation in their initial state packet
        Server.BroadcastBytesToEveryoneExcept(connectPacket.Serialize(), id);

        StatePacket initialStatePacket = new StatePacket();
        initialStatePacket.packetType = StatePacket.PacketType.InitialState;
        initialStatePacket.id = currentPacketId;
        initialStatePacket.clientId = (ushort) id;
        Server.BroadcastBytesTo(initialStatePacket.Serialize(), id);
    }

    public void StartClient()
    {
        Client = new Client();
        Client.PacketReceived += ClientOnPacketReceived;
        
        Client.Connect(Constants.DefaultHost, Constants.DefaultPort);
    }

    private void ClientOnPacketReceived(byte[] data)
    {
        ushort id = StatePacket.GetId(data);

        StatePacket.PacketType packetType = StatePacket.DeserializeType(data);

        GameObject obj;
        switch (packetType)
        {
            case StatePacket.PacketType.State:
                StatePacket.UpdateNetworkState(latestPacketReceived, data);
                
                // Grab the new position of this client's entity and invoke the event to validate client side prediction
                Vector3 clientOwnedPosition = NetworkState.LatestEntityDict[_clientOwnedId].Position;
                ServerPositionReceived?.Invoke(id, clientOwnedPosition);
                LerpT = 0;
                break;
            case StatePacket.PacketType.InitialState:
                _clientOwnedId = StatePacket.GetClientId(data);
                
                StatePacket.UpdateNetworkState(0, data);
                foreach (var pair in NetworkState.LatestEntityDict)
                {
                    obj = Instantiate(PlayerPrefab, pair.Value.Position, pair.Value.Rotation);
                    obj.GetComponent<FollowState>().Id = pair.Key;
                    NetworkedObjects[pair.Key] = obj;

                    if (pair.Key == _clientOwnedId)
                    {
                        obj.AddComponent<ClientInput>();
                    }
                }
                
                _clientOwnedObject = NetworkedObjects[_clientOwnedId];
                // Set the entity that this client owns so that client side prediction knows who to affect
                _clientOwnedObject.GetComponent<FollowState>().SetOwner();

                // Client is now initialized
                _clientInitialized = true;
                break;
            case StatePacket.PacketType.Connect:
                // Get the new entities data from packet
                StatePacket.DeserializeConnectState(data);
                // Create the player's object
                obj = Instantiate(PlayerPrefab);
                obj.GetComponent<FollowState>().Id = id;
                NetworkedObjects[id] = obj;
                break;
            case StatePacket.PacketType.Disconnect:
                Destroy(NetworkedObjects[id]);
                NetworkedObjects.Remove(id);
                break;
        }
        
        if (packetType == StatePacket.PacketType.State || packetType == StatePacket.PacketType.InitialState)
        {
            // Update the latest packet received to the id of the received packet if this is some sort of state packet
            // the connect and disconnect packet id's are different, they tell us the id of the peer
            latestPacketReceived = id;
        }
    }

    private void LateUpdate()
    {
        Server?.PollEvents();
        

        _timer += Time.deltaTime;
        if (Server != null)
        {
            if (_timer >= 1 / StateSendRate)
            {
                _timer = 0;

                StatePacket statePacket = new StatePacket();
                statePacket.packetType = StatePacket.PacketType.State;
                statePacket.id = currentPacketId++;
                Server.BroadcastBytes(statePacket.Serialize());
            }
        }
        else if (Client != null && _clientInitialized)
        {
            if (_timer >= 1 / InputSendRate)
            {
                _timer = 0;

                // Client side prediction
                PreClientInputSend?.Invoke(latestPacketReceived);
                
                // Grab the inputs recorder by Client input and send them
                InputCompressor.Inputs inputs = _clientOwnedObject.GetComponent<ClientInput>().ClientInputs;
                InputPacket inputPacket = InputPacket.ComposePacket(currentPacketId, inputs);
                Client.SendBytes(inputPacket.Serialize());
                ++currentPacketId;
                
                PostClientInputSend?.Invoke(latestPacketReceived);

                // DISABLED FOR CLIENT SIDE PREDICTION
                // InputPacket inputPacket = InputPacket.ComposePacket(currentPacketId++);
                // Client.SendBytes(inputPacket.Serialize());
            }

            LerpT += Time.deltaTime * StateSendRate;
        }
        
        Client?.PollEvents();
    }
    
    private void OnApplicationQuit()
    {
        Server?.Disconnect();
        Client?.Disconnect();
    }

    private class StatePacket
    {
        public enum PacketType : byte {Connect, Disconnect, State, InitialState}

        public PacketType packetType;
        public ushort id;
        // Only used for Initial state packet, it is the peer ID of the client
        public ushort clientId;

        public byte[] Serialize()
        {
            byte[] data = new byte[1]; // The Stream will increase this as needed *lazy...*
            BitStream stream = BitStream.Create(data);
            stream.AutoIncreaseStream = true;

            stream.WriteByte((byte)packetType);
            stream.WriteUInt16(id);
            switch (packetType)
            {
                case PacketType.State:
                    NetworkState.Serialize(stream);
                    break;
                case PacketType.InitialState:
                    stream.WriteUInt16(clientId);
                    NetworkState.Serialize(stream, true);
                    break;
                case PacketType.Connect:
                    NetworkEntity2 newEntity = NetworkState.LatestEntityDict[id];
                    newEntity.Serialize(stream, true);
                    break;
            }
            
            return stream.CloneAsMemoryStream().ToArray();
        }

        // Grab the packet type off the first byte
        public static PacketType DeserializeType(byte[] data)
        {
            return (PacketType)data[0];
        }

        public static ushort GetId(byte[] data)
        {
            // Skip over the first type byte
            return BitConverter.ToUInt16(data, 1);
        }

        public static ushort GetClientId(byte[] data)
        {
            return BitConverter.ToUInt16(data, 3);
        }

        // Used for connect packets, to snap an entity to its initial transform
        public static void DeserializeConnectState(byte[] data)
        {
            BitStream stream  = new BitStream(data);
            PacketType packetType = (PacketType) stream.ReadByte();
            ushort id = stream.ReadUInt16();
            
            NetworkEntity2 entity = NetworkEntity2.Deserialize(stream);

            NetworkState.PreviousEntityDict[id] = entity;
            NetworkState.LatestEntityDict[id] = entity;
        }

        // Update the network state
        public static void UpdateNetworkState(ushort latestPacket, byte[] data)
        {
            BitStream stream  = new BitStream(data);
            PacketType packetType = (PacketType) stream.ReadByte();
            ushort id = stream.ReadUInt16();
            
            // if this is the initial state, there is an extra ushort in there we need to skip over
            // This sucks and State Packet needs to be redesigned to handle different packet type
            // with different data
            if (packetType == PacketType.InitialState)
                stream.ReadUInt16();
            
            if (id > latestPacket)
            {
                NetworkState.Deserialize(stream);
            }
        }
    }

    private struct InputPacket
    {
        public ushort id;
        public byte inputByte;

        public static InputPacket Deserialize(byte[] data)
        {
            InputPacket returnPacket;
            returnPacket.id = BitConverter.ToUInt16(data, 0);
            returnPacket.inputByte = data[2];
            return returnPacket;
        }

        public byte[] Serialize()
        {
            byte[] bytes = new byte[5];
            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, bytes, 0, 2);
            bytes[2] = inputByte;
            return bytes;
        }

        public static InputPacket ComposePacket(ushort currentPacketId, InputCompressor.Inputs inputs)
        {
            InputPacket inputPacket;
            inputPacket.id = currentPacketId;
            inputPacket.inputByte = InputCompressor.CompressInput(inputs);
            return inputPacket;
        }
    }

    private struct ClientInputRecord
    {
        public ushort packetId;
        public InputCompressor.Inputs inputs;
    }
}
