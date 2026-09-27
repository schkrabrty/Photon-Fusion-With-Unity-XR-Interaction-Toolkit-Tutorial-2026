using System.Collections;
using Fusion;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;
using PhotonAppSettings = Fusion.Photon.Realtime.PhotonAppSettings;

// Microphone/platform setup is separate from the Fusion connection lesson.
[RequireComponent(typeof(Recorder), typeof(FusionVoiceClient), typeof(VoiceLogger))]
public class FusionVoiceSetup : MonoBehaviour
{
    private NetworkRunner runner;
    private Recorder recorder;
    private FusionVoiceClient voice;
    private bool microphoneAllowed;

    private void Awake()
    {
        if (FusionNetworkManager.Instance && FusionNetworkManager.Instance.gameObject != gameObject) return;
        runner = GetComponent<NetworkRunner>();
        recorder = GetComponent<Recorder>();
        voice = GetComponent<FusionVoiceClient>();
        voice.PrimaryRecorder = recorder;
        voice.AutoConnectAndJoin = !string.IsNullOrWhiteSpace(PhotonAppSettings.Global.AppSettings.AppIdVoice);
        recorder.RecordWhenJoined = false;
        recorder.RecordingEnabled = false;
        if (voice.AutoConnectAndJoin) StartCoroutine(RequestMicrophonePermission());
    }

    private void Update()
    {
        recorder.RecordingEnabled = microphoneAllowed && runner.IsRunning && voice.Client.InRoom;
    }

    private void OnDisable()
    {
        if (!recorder || !voice) return;
        recorder.RecordingEnabled = false;
        voice.AutoConnectAndJoin = false;
        if (voice.Client.IsConnected) voice.Disconnect();
    }

    private IEnumerator RequestMicrophonePermission()
    {
        if (recorder.SourceType != Recorder.InputSourceType.Microphone)
        {
            microphoneAllowed = true;
            yield break;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
        if (!microphoneAllowed)
        {
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += permission => { if (this) microphoneAllowed = true; };
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
        }
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
        microphoneAllowed = true;
#endif
        yield break;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused || !recorder || recorder.SourceType != Recorder.InputSourceType.Microphone) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
    }

}
