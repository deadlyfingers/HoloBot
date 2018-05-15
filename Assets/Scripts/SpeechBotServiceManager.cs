using HoloToolkit.Unity;
using HoloToolkit.Unity.InputModule;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity3dAzure.BingSpeech;
using Unity3dAzure.BotFramework;
using UnityEngine;
using UnityEngine.UI;

public class SpeechBotServiceManager : MonoBehaviour {

  [SerializeField]
  private string targetName;

  [SerializeField]
  private MicRecorder micRecorder;

  [SerializeField]
  private BingSpeechService speechService;

  [SerializeField]
  private BotService botService;

  public AudioSource SoundStartListening;
  public AudioSource SoundStopListening;

  [SerializeField]
  private TextToSpeech textToSpeech;

  // If bot is not being looked at for some time then we should turn everything off
  public uint InactiveTimeLimit = 5;
  private float timer = 0;

  // Text box
  public bool isCaptionsOn = true;
  private bool needsUpdate = false;
  private string botMessage;
  [SerializeField]
  private Text textbox;

  private List<string> queue = new List<string>();

  // state
  private bool isAttached = false;
  private bool isFocused = false;
  private bool hasFocusTriggered = false;
  private string currentTargetName;

  private bool botSocketIsReady = false;
  private bool speechSocketIsReady = false;

  private bool botSocketIsClosed = true;
  private bool speechSocketIsClosed = true;

  private bool shouldStartRecording = false;
  private bool shouldStopRecording = false;

  private bool shouldStartServices = false;

  private bool isStartingServices = false;

  // Use this for initialization
  void Start() {
    if (micRecorder == null) {
      Debug.LogError("Requires mic recorded component");
    }
    if (speechService == null) {
      Debug.LogError("Requires Speech Service component");
    }
    if (botService == null) {
      Debug.LogError("Requires Bot Service component");
    }

    if (textbox == null) {
      Debug.LogWarning("Textbox not set - captions will be turned off");
      isCaptionsOn = false;
    }
  }

  // Update is called once per frame
  void Update() {
    if (!isAttached) {
      AttachListeners();
    }
    // text
    if (needsUpdate) {
      textbox.text = isCaptionsOn ? botMessage : "";
      // speak text
      if (textToSpeech != null) {
        Debug.Log("*** bot says *** " + botMessage);
        textToSpeech.StartSpeaking(botMessage);
      }
      needsUpdate = false;
    }
    // microphone control
    if (shouldStartRecording) {
      shouldStartRecording = false;
      StartRecording();
    } else if (shouldStopRecording) {
      shouldStopRecording = false;
      StopRecording();
    }
    // send next query message to bot
    if (queue.Count >= 1) {
      Query(queue[0]);
      queue.RemoveAt(0);
    }
    // start services when sockets are available
    if (shouldStartServices && !speechSocketIsReady && !botSocketIsReady) {
      Debug.Log("Waiting to close sockets first then opening...");
      StartAllServices();
      shouldStartServices = false;
    }
    // Turn off services after bot has lost focus for some time (run timer only while bot is active)
    if (isFocused || !botSocketIsReady || !speechSocketIsReady) {
      return;
    }
    timer += Time.deltaTime;
    if (!isFocused && timer > InactiveTimeLimit) {
      Debug.Log("InactiveTimeLimit: " + timer);
      StopAllServices();
      timer = 0;
    }
  }

  void OnEnable() {
    AttachListeners();
  }

  void OnDisable() {
    DettachListeners();
  }

  private void AttachListeners() {
    if (isAttached) {
      return;
    }
    if (FocusManager.IsInitialized) {
      FocusManager.Instance.PointerSpecificFocusChanged += HandlePointerSpecificFocusChanged;
      BingSpeechService.OnSocketSpeechConfigCompleted += HandleSocketSpeechConfigCompleted;
      BingSpeechService.OnPartialTextReceived += HandleBingSpeechPartialText;
      BingSpeechService.OnPhraseTextReceived += HandleBingSpeechPhraseText;
      BingSpeechService.OnSpeechSocketClosed += HandleSpeechSocketClosed;
      BotService.OnBotSocketOpened += HandleBotSocketOpen;
      BotService.OnBotMessageReceived += HandleBotMessage;
      BotService.OnBotSocketClosed += HandleBotSocketClosed;
      MicRecorder.OnRecordingStopped += HandleRecordingStopped;
      isAttached = true;
    }
  }

  private void DettachListeners() {
    if (!isAttached) {
      return;
    }
    if (FocusManager.IsInitialized) {
      FocusManager.Instance.PointerSpecificFocusChanged -= HandlePointerSpecificFocusChanged;
      BingSpeechService.OnSocketSpeechConfigCompleted -= HandleSocketSpeechConfigCompleted;
      BingSpeechService.OnPartialTextReceived -= HandleBingSpeechPartialText;
      BingSpeechService.OnPhraseTextReceived -= HandleBingSpeechPhraseText;
      BingSpeechService.OnSpeechSocketClosed -= HandleSpeechSocketClosed;
      BotService.OnBotSocketOpened -= HandleBotSocketOpen;
      BotService.OnBotMessageReceived -= HandleBotMessage;
      BotService.OnBotSocketClosed -= HandleBotSocketClosed;
      MicRecorder.OnRecordingStopped -= HandleRecordingStopped;
      isAttached = false;
    }
  }

  private void HandlePointerSpecificFocusChanged(IPointingSource pointer, GameObject oldFocusedObject, GameObject newFocusedObject) {
    if (newFocusedObject == null) {
      isFocused = false;
      currentTargetName = "";
      return;
    }
    if (string.Equals(newFocusedObject.name, targetName)) {
      isFocused = true;
      if (!hasFocusTriggered) {
        currentTargetName = newFocusedObject.name;
        Debug.Log("*** Focus triggered *** " + newFocusedObject.name);
        hasFocusTriggered = true;
        shouldStartServices = true; //StartAllServices();
      }
    } else {
      isFocused = false;
      currentTargetName = "";
    }
  }

  private void HandleBotSocketOpen() {
    Debug.Log("BOT GO :]");
    botSocketIsReady = true;
    botSocketIsClosed = false;
    CheckAllSocketsAreReady();
  }

  private void HandleSocketSpeechConfigCompleted() {
    Debug.Log("BINGO ;)");
    speechSocketIsReady = true;
    speechSocketIsClosed = false;
    CheckAllSocketsAreReady();
  }

  private bool CheckAllSocketsAreReady() {
    if (botSocketIsReady && speechSocketIsReady) {
      Debug.Log("ALL SYSTEMS GO! :O");
      shouldStartRecording = true;
      return true;
    }
    return false;
  }

  private void HandleBotSocketClosed() {
    botSocketIsClosed = true;
    botSocketIsReady = false;
    // if one of the socket closes unexpectedly then try to reopen
    if (isStartingServices) {
      Debug.LogWarning("Trying to reconnect bot socket!");
      botService.Connect();
    }
    CheckAllSocketsAreClosed();
  }

  private void HandleSpeechSocketClosed() {
    speechSocketIsClosed = true;
    speechSocketIsReady = false;
    // if one of the socket closes unexpectedly then try to reopen
    if (isStartingServices) {
      Debug.LogWarning("Trying to reconnect speech socket!");
      speechService.Connect();
    }
    CheckAllSocketsAreClosed();
  }

  private bool CheckAllSocketsAreClosed() {
    if (botSocketIsClosed && speechSocketIsClosed) {
      Debug.Log("ALL SYSTEMS STOPPED! :p");
      hasFocusTriggered = false;
      shouldStopRecording = true;
      return true;
    }
    return false;
  }

  private void StartRecording() {
    Debug.Log("PING - start recording...");
    if (SoundStartListening != null) {
      SoundStartListening.Play();
    }
    micRecorder.StartRecording();
  }

  private void StopRecording() {
    Debug.Log("PING - stop recording!");
    if (SoundStopListening != null && micRecorder.IsRecording) {
      SoundStopListening.Play();
    }
    if (micRecorder.IsRecording) {
      micRecorder.Stop();
    }
  }

  private void HandleBingSpeechPartialText(string message) {
    SetCaptionsText(message);
  }

  private void HandleBingSpeechPhraseText(string message) {
    SetCaptionsText(message);
    // add query to queue
    queue.Add(message);
  }

  private void Query(string message) {
    // Relay message to bot
    botService.SendBotMessage(message);
  }

  private void HandleBotMessage(string message) {
    SetCaptionsText(message);
  }

  public void SetCaptionsText(string message) {
    if (message == null) {
      return;
    }
    
    //if (captionsManager != null)
    //{
    //    captionsManager.SetCaptionsText(message);
    //}
    botMessage = message;
    if (textbox != null) {
      needsUpdate = true;
    }



    // detect if bye then end session

    //StopListeningIfSessionIsOver();
  }

  private void HandleRecordingStopped() {
    Debug.Log("*** STOPPED LISTENING ***");
  }

  private void StopListeningIfSessionIsOver() {
    if (!string.Equals(currentTargetName, targetName)) {
      Debug.Log("==================================================================\nSESSION OVER current target: " + currentTargetName);
      StopAllServices();
    }
  }

  // Start web socket services and start mic recording
  // 1. Start Bot service & Bing Speech web socket service
  // 2. Await sending Speech Config completed
  // 3. Play "ping" audio is recording cue 
  // 4. Start recording mic
  private void StartAllServices() {
    isStartingServices = true;
    botService.Connect();
    speechService.Connect();
    hasFocusTriggered = true;
  }

  private void StopAllServices() {
    isStartingServices = false;
    StopRecording();
    botService.Close();
    speechService.Close();
  }




}
