﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using ENet;
using UnityEngine;
using EventType = ENet.EventType;

public class Server
{
    public event Action<byte[], uint> PacketReceived;
    public event Action<uint> PeerConnected;
    public event Action<uint> PeerDisconncted;
    
    private Host _server = new Host();
    private Address _address = new Address();
    private ENet.Event _netEvent;

    private Dictionary<uint, Peer> _peerDict = new Dictionary<uint, Peer>();

    public void Listen(ushort port, string host = null)
    {
        if (host != null)
            _address.SetHost(host);
        _address.Port = port;
        _server.Create(_address, Constants.DefaultPeerLimit, Constants.ChannelLimit);
        
        Debug.Log("Server created on: " + host + ", Port: " + port);
    }

    public void BroadcastBytes(byte[] data)
    {
        Packet packet = default(Packet);
        packet.Create(data);
        _server.Broadcast(0, ref packet);
    }
    public void BroadcastBytes(byte[] data, uint peerId)
    {
        Packet packet = default(Packet);
        packet.Create(data);
        _server.Broadcast(0, ref packet, new Peer[] { _peerDict[peerId] });
    }

    public void PollEvents()
    {
        bool polled = false;

        while (!polled)
        {
            if (_server.CheckEvents(out _netEvent) <= 0)
            {
                if (_server.Service(0, out _netEvent) <= 0)
                    return;

                polled = true;
            }

            switch (_netEvent.Type)
            {
                case EventType.None:
                    break;
                case EventType.Connect:
                    PeerConnected?.Invoke(_netEvent.Peer.ID);
                    _peerDict[_netEvent.Peer.ID] = _netEvent.Peer;
                    Debug.Log("Client connected - ID: " + _netEvent.Peer.ID + ", IP: " + _netEvent.Peer.IP);
                    break;
                case EventType.Disconnect:
                    PeerDisconncted?.Invoke(_netEvent.Peer.ID);
                    _peerDict.Remove(_netEvent.Peer.ID);
                    Debug.Log("Client disconnected - ID: " + _netEvent.Peer.ID + ", IP: " + _netEvent.Peer.IP);
                    break;
                case EventType.Receive:
                    byte[] buffer = new byte[_netEvent.Packet.Length];
                    _netEvent.Packet.CopyTo(buffer);
                    PacketReceived?.Invoke(buffer, _netEvent.Peer.ID);
                    break;
                case EventType.Timeout:
                    Debug.Log("Client timed out - IP: " + _netEvent.Peer.IP);
                    break;
                default:
                    break;
            }
        }
    }

    ~Server()
    {
        _server.Flush();
        _server.Dispose();
    }
}
