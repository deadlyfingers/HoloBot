using HoloToolkit.Unity.InputModule;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity3dAzure.BingSpeech;
using Unity3dAzure.BotFramework;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(AudioSource))]
public class SpeechBotServiceManager : MonoBehaviour {

    [SerializeField]
    private string targetName;

    [SerializeField]
    private MicRecorder micRecorder;

    [SerializeField]
    private BingSpeechService speechService;

    [SerializeField]
    private BotService botService;

    [SerializeField]
    public AudioSource audioSource;

    // Text box
    public bool isCaptionsOn = true;
    private bool needsUpdate = false;
    private string botMessage;
    [SerializeField]
    private Text textbox;

    private List<string> queue = new List<string>();

    // state
    private bool isAttached = false;
    private bool hasFocusTriggered = false;
    private string currentTargetName;

    private bool botSocketIsDeployable = true;
    private bool speechSocketIsDeployable = true;

    // Use this for initialization
    void Start () {
		if (micRecorder == null)
        {
            Debug.LogError("Requires mic recorded component");
        }
        if (speechService == null)
        {
            Debug.LogError("Requires Speech Service component");
        }
        if (botService == null)
        {
            Debug.LogError("Requires Bot Service component");
        }
        if (audioSource == null)
        {
            gameObject.GetComponent<AudioSource>();
        }
    }
	
	// Update is called once per frame
	void Update () {
		if (!isAttached)
        {
            AttachListeners();
        }
        if (needsUpdate)
        {
            textbox.text = isCaptionsOn ? botMessage : "";
            needsUpdate = false;
        }

        if (queue.Count >= 1 )
        {
            Query(queue[0]);
            queue.RemoveAt(0);
        }
    }

    void OnEnable()
    {
        AttachListeners();
    }

    void OnDisable()
    {
        DettachListeners();
    }

    private void AttachListeners()
    {
        if (isAttached)
        {
            return;
        }
        if (FocusManager.IsInitialized)
        {
            FocusManager.Instance.PointerSpecificFocusChanged += HandlePointerSpecificFocusChanged;
            BingSpeechService.OnSocketSpeechConfigCompleted += HandleSocketSpeechConfigCompleted;
            BingSpeechService.OnPartialTextReceived += HandleBingSpeechPartialText;
            BingSpeechService.OnPhraseTextReceived += HandleBingSpeechPhraseText;
            BingSpeechService.OnSpeechSocketClosed += HandleSpeechSocketClosed;
            BotService.OnBotMessageReceived += HandleBotMessage;
            BotService.OnBotSocketClosed += HandleBotSocketClosed;
            MicRecorder.OnRecordingStopped += HandleRecordingStopped;
            isAttached = true;
        }
    }

    private void DettachListeners()
    {
        if (!isAttached)
        {
            return;
        }
        if (FocusManager.IsInitialized)
        {
            FocusManager.Instance.PointerSpecificFocusChanged -= HandlePointerSpecificFocusChanged;
            BingSpeechService.OnSocketSpeechConfigCompleted -= HandleSocketSpeechConfigCompleted;
            BingSpeechService.OnPartialTextReceived -= HandleBingSpeechPartialText;
            BingSpeechService.OnPhraseTextReceived -= HandleBingSpeechPhraseText;
            BingSpeechService.OnSpeechSocketClosed -= HandleSpeechSocketClosed;
            BotService.OnBotMessageReceived -= HandleBotMessage;
            BotService.OnBotSocketClosed -= HandleBotSocketClosed;
            MicRecorder.OnRecordingStopped -= HandleRecordingStopped;
            isAttached = false;
        }
    }

    private void HandlePointerSpecificFocusChanged(IPointingSource pointer, GameObject oldFocusedObject, GameObject newFocusedObject)
    {
        if (newFocusedObject == null)
        {
            currentTargetName = "";
            return;
        }
        if (string.Equals(newFocusedObject.name, targetName)) {
            if (!hasFocusTriggered)
            {
                currentTargetName = newFocusedObject.name;
                Debug.Log("Focus triggered:" + newFocusedObject.name);
                // Start web socket services and start mic recording
                // 1. Start Bot service & Bing Speech web socket service
                // 2. Await sending Speech Config completed
                // 3. Play audio recording cue 
                // 4. Start recording mic
                botService.Connect();
                speechService.Connect();
                hasFocusTriggered = true;
            }
        }
        else
        {
            currentTargetName = "";
        }
    }


    private void HandleSocketSpeechConfigCompleted()
    {
        // play cue to let user know we are ready to record!
        Debug.Log("PING");
        micRecorder.ShouldStartRecording();
    }




    private void HandleBingSpeechPartialText(string message)
    {
        SetCaptionsText(message);
    }



    private void HandleBingSpeechPhraseText(string message)
    {
        SetCaptionsText(message);
        // add query to queue
        queue.Add(message);
    }

    private void Query(string message)
    {
        // Relay message to bot
        botService.SendBotMessage(message);
    }

    private void HandleBotMessage(string message)
    {
        SetCaptionsText(message);
        StopListeningIfSessionIsOver();
    }

    public void SetCaptionsText(string message)
    {
        if (message == null)
        {
            return;
        }
        Debug.Log("*** bot says *** " + message);
        //if (captionsManager != null)
        //{
        //    captionsManager.SetCaptionsText(message);
        //}
        botMessage = message;
        if (textbox != null)
        {
            needsUpdate = true;
        }
    }

    private void HandleRecordingStopped()
    {
        Debug.Log("*** STOPPED LISTENING ***");
    }

    private void StopListeningIfSessionIsOver()
    {
        if (!string.Equals(currentTargetName, targetName)) {
            Debug.Log("==================================================================\nSESSION OVER current target: " + currentTargetName);
            //micRecorder.ShouldStopRecording();

            //speechService.Close();
            //botService.Close();

            // wait till sockets are closed before reopening 
            //hasFocusTriggered = false;
        }
    }

    private void HandleBotSocketClosed()
    {
        botSocketIsDeployable = true;
        ValidateAllSocketsAreDeployable();
    }

    private void HandleSpeechSocketClosed()
    {
        speechSocketIsDeployable = true;
        ValidateAllSocketsAreDeployable();
    }

    private void ValidateAllSocketsAreDeployable()
    {
        if (botSocketIsDeployable && speechSocketIsDeployable)
        {
            Debug.Log("ALL SYSTEMS GO!");
            hasFocusTriggered = false;
        }
    }


}
