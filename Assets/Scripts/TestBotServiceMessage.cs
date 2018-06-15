using System.Collections;
using System.Collections.Generic;
using Unity3dAzure.BotFramework;
using UnityEngine;

public class TestBotServiceMessage : MonoBehaviour {

  private BotService botService;

  // Use this for initialization
  void Start() {
    botService = gameObject.GetComponent<BotService>();
  }

  // Update is called once per frame
  void Update() {

  }

  public void TestSendMessage(string message) {
    if (botService == null) {
      Debug.LogWarning("Expected BotService component to test message with");
      return;
    }
    if (!string.IsNullOrEmpty(message)) {
      Debug.Log("Test send message: " + message);
      botService.SendBotMessage(message);
    }
  }
}
