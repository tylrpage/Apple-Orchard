﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LoginUIController : MonoBehaviour
{
    // Raised when login is successful, arguments are the username and the user id
    public static Action<string, uint> OnLoginSuccess;
    
    [SerializeField] private InputField _displayName;
    [SerializeField] private InputField _password;
    [SerializeField] private Button LoginButton;
    [SerializeField] private Button RegisterButton;

    private bool _usernameValid = false;
    private bool _passwordValid = false;

    // Start is called before the first frame update
    void Start()
    {
        LoginButton.onClick.AddListener(OnLoginClick);
        RegisterButton.onClick.AddListener(OnRegisterClick);
        
        _displayName.onValueChanged.AddListener(ValidateUsername);
        _password.onValueChanged.AddListener(ValidatePassword);
        
        SetLoginAndRegisterInteractable(_usernameValid, _passwordValid);
    }

    private void ValidatePassword(string password)
    {
        _passwordValid = password.Length > 6 &&
                         password.Length < 30;
        
        SetLoginAndRegisterInteractable(_usernameValid, _passwordValid);
    }

    private void ValidateUsername(string username)
    {
        _usernameValid = username.Length > 3 &&
                         username.Length < 18;

        SetLoginAndRegisterInteractable(_usernameValid, _passwordValid);
    }

    private void SetLoginAndRegisterInteractable(bool userValid, bool passwordValid)
    {
        if (userValid && passwordValid)
        {
            LoginButton.interactable = true;
            RegisterButton.interactable = true;
        }
        else
        {
            LoginButton.interactable = false;
            RegisterButton.interactable = false;
        }
    }

    private void OnRegisterClick()
    {
        // Send the HTTP Request
        StartCoroutine(Register(_displayName.text, _password.text));
    }

    private void OnLoginClick()
    {
        // Send the HTTP Request
        StartCoroutine(Login(_displayName.text, _password.text, OnLoginSuccess));
    }

    private IEnumerator Register(string username, string password)
    {
        WWWForm form = new WWWForm();
        form.AddField("username", username);
        form.AddField("password", password);

        using (UnityWebRequest www = UnityWebRequest.Post(Constants.PHPServerHost + "/register.php", form))
        {
            // Wait for request to come back
            yield return www.SendWebRequest();

            if (www.isNetworkError || www.isHttpError)
            {
                Debug.Log(www.error + www.downloadHandler.text);
            }
            else
            {
                Debug.Log("Registration complete");
            }
        }
    }

    private IEnumerator Login(string username, string password, Action<string, uint> successEvent)
    {
        WWWForm form = new WWWForm();
        form.AddField("username", username);
        form.AddField("password", password);

        using (UnityWebRequest www = UnityWebRequest.Post(Constants.PHPServerHost + "/login.php", form))
        {
            // Wait for request to come back
            yield return www.SendWebRequest();

            if (www.isNetworkError || www.isHttpError)
            {
                Debug.Log(www.error + www.downloadHandler.text);
            }
            else
            {
                Debug.Log("Login complete");
                successEvent.Invoke(username, UInt32.Parse(www.downloadHandler.text));
            }
        }
    }
}