using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ReadmeCapture
{
    private static string output = Environment.GetEnvironmentVariable("FUSION_DOCS_OUTPUT")
        ?? Path.GetFullPath("docs/images");
    private static int stage;
    private static double next;
    private static Camera camera;
    public static void Capture()
    {
        ShaderUtil.allowAsyncCompilation = false;
        Directory.CreateDirectory(output);
        EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity");
        foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
        camera = new GameObject("Documentation Camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 55;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        File.WriteAllText(output + "/capture-positions.txt", string.Join("\n", new[]{"Table","Interactable Cube","Grabbable Cube","XR Origin (XR Rig)"}.Select(n => n + ": " + GameObject.Find(n)?.transform.position)));
        next = EditorApplication.timeSinceStartup + 3;
        EditorApplication.update += Step;
    }
    private static void Step()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (stage == 0)
            {
                var table = GameObject.Find("Table").transform.position;
                Take("game-overview.png", table + new Vector3(0.9f,1.0f,-1.55f), table + Vector3.up * 0.2f);
            }
            else if (stage == 1)
            {
                var a = GameObject.Find("Interactable Cube").transform.position;
                var b = GameObject.Find("Grabbable Cube").transform.position;
                var middle = (a+b)*0.5f;
                Take("cube-lessons.png", middle+new Vector3(0.32f,0.3f,-0.65f), middle);
            }
            else if (stage == 2)
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Lobby Scene.unity");
                foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
                camera = new GameObject("Documentation Camera").AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = 55;
                camera.nearClipPlane = 0.03f;
                next = EditorApplication.timeSinceStartup + 3;
                stage++;
                return;
            }
            else if (stage == 3)
            {
                var manager = UnityEngine.Object.FindFirstObjectByType<FusionLobbyUI>();
                manager.PlayerCountText.gameObject.SetActive(true);
                manager.CountdownText.gameObject.SetActive(true);
                var canvas = manager.LobbyCanvas;
                File.AppendAllText(output+"/capture-positions.txt", "\nLobby Canvas: "+canvas.position+" rotation "+canvas.eulerAngles+" scale "+canvas.lossyScale);
                Take("waiting-room.png", canvas.position - canvas.forward * 3.0f, canvas.position);
            }
            else
            {
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
                return;
            }
            stage++;
            next = EditorApplication.timeSinceStartup + 2;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(output+"/capture-error.txt", e.ToString());
            EditorApplication.Exit(1);
        }
    }
    private static void Take(string name, Vector3 position, Vector3 target)
    {
        camera.transform.position=position;
        camera.transform.LookAt(target);
        camera.aspect=16f/9f;
        // Face the existing text toward the documentation camera, as CanvasController does in play mode.
        foreach (var label in UnityEngine.Object.FindObjectsByType<CanvasController>(FindObjectsSortMode.None))
        {
            label.transform.LookAt(camera.transform);
            label.transform.Rotate(0,180,0);
        }
        Canvas.ForceUpdateCanvases();
        var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);
        rt.Create();
        var request=new UniversalRenderPipeline.SingleCameraRequest { destination=rt };
        for (int warmup = 0; warmup < 12; warmup++) RenderPipeline.SubmitRenderRequest(camera,request);
        var old=RenderTexture.active;
        RenderTexture.active=rt;
        var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1920,1080),0,0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());
        RenderTexture.active=old;
        UnityEngine.Object.DestroyImmediate(texture);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        Debug.Log("README capture: "+name);
    }
}
