using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System;
using Unity3dAzure.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Unity3dAzure.BotFramework {
  public class BotService : UnityWebSocket {
        // Bot Service message delegate
        public delegate void BotMessageReceived(string message);
        public static BotMessageReceived OnBotMessageReceived;

        public delegate void UserMessageReceived(string message);
        public static UserMessageReceived OnUserMessageReceived;

        // socket delegate
        public delegate void BotSocketClosed();
        public static BotSocketClosed OnBotSocketClosed;

        [Header("Connect with Direct Line secret key")]
    [SerializeField]
    private string DirectLineSecretKey; // Direct Line secret key

    [Header("Connect with a generated token")]
    [SerializeField]
    private string Token;

    // Conversation state
    private string ConversationId;
    private string Watermark;

    // Token timer
    private const uint EXPIRES_IN = 1800; // max time to refresh token
    private float MaxTime = EXPIRES_IN;
    private float Timer = 0; // time since token was generated
    private uint TimeBefore = 5; // amount of seconds to refresh token before MaxTime is up
    private bool HasTimerStarted = false;

    [Space]
    [SerializeField]
    private string UserName = "UnityUser";

    [SerializeField]
    private bool AutoConnect = false;

    // Use this for initialization
    void Start() {
      if (AutoConnect) {
        Connect();
      }
    }

    void Update() {
      if (!HasTimerStarted) {
        return;
      }
      Timer += Time.deltaTime;
      if (Timer > MaxTime) {
        // refresh
        HasTimerStarted = false;
        Timer = 0;
        RefreshToken();
      }
    }

    public void Stop() {
      HasTimerStarted = false;
      Timer = 0;
      // close web socket
      Close();
    }

    #region Refresh Token

    private void UpdateToken(TokenResponse response) {
      StartUsingToken(response.token, response.conversationId, response.expires_in);
    }

    public void StartUsingToken(string token, string conversationId = null, uint expiresIn = EXPIRES_IN) {
      Token = token;
      ConversationId = conversationId;
      if (expiresIn > 0) {
        MaxTime = expiresIn - TimeBefore;
      }
      // restart Update timer
      Timer = 0;
      HasTimerStarted = true;
    }

    public void RefreshToken() {
      if (HasTimerStarted) {
        return;
      }
      StartCoroutine(PostRefreshToken(Token));
    }

    private IEnumerator PostRefreshToken(string token) {
      Debug.Log("*** Refesh token *** " + Timer);
      using (UnityWebRequest www = UnityWebRequest.Post("https://directline.botframework.com/v3/directline/tokens/refresh", "")) {
        www.SetRequestHeader("Authorization", "Bearer " + token);
        www.chunkedTransfer = false;
        yield return www.SendWebRequest();
        if (www.isHttpError || www.isNetworkError) {
          Debug.LogError("Refresh request error: " + www.error + " status:" + www.responseCode.ToString());
        } else {
          Debug.Log("Refresh request received:\n" + www.downloadHandler.text);
          try {
            TokenResponse response = JsonUtility.FromJson<TokenResponse>(www.downloadHandler.text);
            UpdateToken(response);
          } catch (Exception ex) {
            Debug.LogError("Failed to parse token response:" + ex.Message + " body:" + www.downloadHandler.text);
          }
        }
      }
    }

    #endregion

    private void StartConversation() {
      if (!string.IsNullOrEmpty(DirectLineSecretKey)) {
        StartConversationWithToken(DirectLineSecretKey);
      } else if (!string.IsNullOrEmpty(Token)) {
        StartConversationWithToken(Token);
      } else {
        Debug.LogWarning("Direct Line secret key or token required");
      }
    }

    private void StartConversationWithToken(string token) {
      if (!string.IsNullOrEmpty(ConversationId) || !string.IsNullOrEmpty(Watermark)) {
        // resume conversation
        StartCoroutine(GetConversation(token, ConversationId, Watermark));
      } else {
        // start conversation
        StartCoroutine(PostConversation(token));
      }
    }

    private IEnumerator PostConversation(string token) {
      using (UnityWebRequest www = UnityWebRequest.Post("https://directline.botframework.com/v3/directline/conversations", "")) {
        www.SetRequestHeader("Authorization", "Bearer " + token);
        www.chunkedTransfer = false;
        yield return www.SendWebRequest();
        if (www.isHttpError || www.isNetworkError) {
          Debug.LogError("Post Conversation request error: " + www.error + " status:" + www.responseCode.ToString());
        } else {
          Debug.Log("Post Conversations request received:\n" + www.downloadHandler.text);
          try {
            TokenResponse response = JsonUtility.FromJson<TokenResponse>(www.downloadHandler.text);
            UpdateToken(response);
            ConnectWebSocketWithUrl(response.streamUrl);
          } catch (Exception ex) {
            Debug.LogError("Failed to parse token response:" + ex.Message + " body:" + www.downloadHandler.text);
          }
        }
      }
    }

    private IEnumerator GetConversation(string token, string conversationId, string watermark) {
      string url = "https://directline.botframework.com/v3/directline/conversations/" + conversationId;
      if (!string.IsNullOrEmpty(watermark)) {
        url = url + "?watermark=" + watermark;
      }
      using (UnityWebRequest www = UnityWebRequest.Get(url)) {
        www.SetRequestHeader("Authorization", "Bearer " + token);
        yield return www.SendWebRequest();
        if (www.isHttpError || www.isNetworkError) {
          Debug.LogError("Get Conversations request error: " + www.error + " status:" + www.responseCode.ToString() + " Conversation id:" + conversationId + " Watermark:" + watermark + " Token:\n" + token);
        } else {
          Debug.Log("Get Conversations request received:\n" + www.downloadHandler.text);
          try {
            TokenResponse response = JsonUtility.FromJson<TokenResponse>(www.downloadHandler.text);
            UpdateToken(response);
            ConnectWebSocketWithUrl(response.streamUrl);
          } catch (Exception ex) {
            Debug.LogError("Failed to parse token response:" + ex.Message + " body:" + www.downloadHandler.text);
          }
        }
      }
    }

    public override void SendInputText(InputField input) {
      if (input == null) {
        Debug.LogError("No input field set");
        return;
      }
      SendBotMessage(input.text);
    }

    public void SendBotMessage(string message) {
      if (_ws == null || !_ws.IsOpen()) {
        Debug.LogWarning("Web socket not open. A web socket connection is required to receive responses from the bot.");
        // allow to continue
      }
      if (string.IsNullOrEmpty(message)) {
        Debug.LogWarning("Error no message");
        return;
      }
      if (string.IsNullOrEmpty(ConversationId))
            {
                Debug.LogError("Error no conversation id");
                return;
            }
      if (!string.IsNullOrEmpty(DirectLineSecretKey)) {
        Debug.Log("Send message using secret:" + message + " ConversationId:" + ConversationId);
        StartCoroutine(PostMessage(DirectLineSecretKey, message, ConversationId, UserName));
      } else if (!string.IsNullOrEmpty(Token)) {
        Debug.Log("Send message using token:" + message + " ConversationId:" + ConversationId);
        StartCoroutine(PostMessage(Token, message, ConversationId, UserName));
      } else {
        Debug.LogError("Unable to send message. A Direct Line secret key or token is required.");
      }
    }

    private IEnumerator PostMessage(string token, string message, string conversationId, string user) {
      UserActivity activity = UserActivity.CreateMessage(message, user);
      string json = JsonUtility.ToJson(activity);
      byte[] bytes = Encoding.UTF8.GetBytes(json);
      string url = "https://directline.botframework.com/v3/directline/conversations/" + conversationId + "/activities";
      Debug.Log("activity:" + json + "url:" + url);
      using (UnityWebRequest www = new UnityWebRequest(url)) {
        www.SetRequestHeader("Authorization", "Bearer " + token);
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Accept", "application/json");
        www.chunkedTransfer = false;
        www.uploadHandler = new UploadHandlerRaw(bytes);
        www.uploadHandler.contentType = "application/json";
        www.downloadHandler = new DownloadHandlerBuffer();
        www.method = UnityWebRequest.kHttpVerbPOST;
        yield return www.SendWebRequest();
        if (www.isHttpError || www.isNetworkError) {
          Debug.LogError("Post Message error: " + www.error + " status:" + www.responseCode.ToString());
        } else {
          Debug.Log("Sent message to bot:\n" + www.downloadHandler.text);
        }
      }
    }

    private void ConnectWebSocketWithUrl(string url) {
      Debug.Log("Connect Web Socket with url: " + url);
      WebSocketUri = url;
      ConnectWebSocket();
    }

    #region Web Socket methods

    public override void Connect() {
      StartConversation();
    }

    #endregion

    #region Web Socket handlers

    protected override void OnWebSocketOpen(object sender, EventArgs e) {
      Debug.Log("Bot web socket is open");
    }

    protected override void OnWebSocketClose(object sender, WebSocketCloseEventArgs e) {
      Debug.Log("Bot web socket closed with reason: " + e.Reason );
      DettachHandlers();
            if (OnBotSocketClosed != null)
            {
                OnBotSocketClosed();
            }
    }

    protected override void OnWebSocketMessage(object sender, WebSocketMessageEventArgs e) {
      Debug.LogFormat("Bot web socket {1} message:\n{0}", e.Data, e.IsBinary ? "binary" : "string");

            // ignore empty messages
            if (String.IsNullOrEmpty(e.Data))
            {
                return;
            }

            // parse activities message
            MessageActivities response = ParseMessageActivities(e.Data);
            if (response.activities == null || response.activities.Length < 1)
            {
                Debug.LogWarning("No activities message found:\n" + e.Data);
                return;
            }

            // Update watermark id
            Watermark = response.watermark;
            Debug.Log("Watermark id:" + Watermark);

            // Handle bot is typing status message - type: "typing"
            if (String.IsNullOrEmpty(response.activities[0].text) && string.Equals(response.activities[0].type, "typing"))
            {
                Debug.Log("Bot is typing...");
                RaiseOnBotMessageReceived("Thinking...");
                return;
            }

            if (response.activities.Length > 1)
            {
                Debug.LogWarning("Handle case if more than 1 activity is received.");
            }

            MessageActivity messageActivity = response.activities[0];

            // decide what to do depending on message path type
            if (String.IsNullOrEmpty(messageActivity.inputHint))
            {
                // user
                RaiseOnUserMessageReceived(messageActivity);
            }
            else if (!String.IsNullOrEmpty(messageActivity.inputHint))
            {
                // bot
                RaiseOnBotMessageReceived(messageActivity);
            }
            else
            {
                Debug.LogWarning("Unhandled message type: " + messageActivity.inputHint + " message: " + messageActivity.text);
            }
    }


        protected override void OnWebSocketError(object sender, WebSocketErrorEventArgs e) {
      Debug.LogError("Bot web socket error: " + e.Message);
      DisconnectWebSocket();
    }

        #endregion

        #region Parse JSON body helpers

        public static MessageActivities ParseMessageActivities(string json)
        {
            try
            {
                return JsonUtility.FromJson<MessageActivities>(json);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarningFormat("Failed to parse bot message. Reason: {0} \n'{1}'", exception.Message, json);
                return null;
            }
        }

        #endregion

        #region Events

        private void RaiseOnBotMessageReceived(MessageActivity messageActivity)
        {
            if (OnBotMessageReceived != null)
            {
                OnBotMessageReceived(messageActivity.text);
            }
        }

        private void RaiseOnBotMessageReceived(string message)
        {
            if (OnBotMessageReceived != null)
            {
                OnBotMessageReceived(message);
            }
        }

        private void RaiseOnUserMessageReceived(MessageActivity messageActivity)
        {
            if (OnUserMessageReceived != null)
            {
                OnUserMessageReceived(messageActivity.text);
            }
        }


        #endregion

    }
}
