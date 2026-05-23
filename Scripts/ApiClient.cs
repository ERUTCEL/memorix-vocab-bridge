// ApiClient.cs
// localhost:8000 FastAPI 서버와 통신하는 HTTP 유틸리티
// UnityWebRequest 기반 코루틴 패턴

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class ApiClient
{
    public const string BASE_URL = "http://localhost:8000";

    // ── GET ──────────────────────────────────────────────────────

    public static IEnumerator Get<T>(
        string endpoint,
        Action<T> onSuccess,
        Action<string> onError = null)
    {
        string url = BASE_URL + endpoint;
        using var req = UnityWebRequest.Get(url);
        req.timeout = 30;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"GET {endpoint} 실패: {req.error}";
            Debug.LogWarning(err);
            onError?.Invoke(err);
            yield break;
        }

        string json = req.downloadHandler.text;
        T result = JsonUtility.FromJson<T>(json);
        onSuccess?.Invoke(result);
    }

    // ── POST (JSON body) ─────────────────────────────────────────

    public static IEnumerator Post<T>(
        string endpoint,
        object body,
        Action<T> onSuccess,
        Action<string> onError = null)
    {
        string url = BASE_URL + endpoint;
        string bodyJson = JsonUtility.ToJson(body);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(bodyBytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 60;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"POST {endpoint} 실패: {req.error} / {req.downloadHandler.text}";
            Debug.LogWarning(err);
            onError?.Invoke(err);
            yield break;
        }

        string json = req.downloadHandler.text;
        T result = JsonUtility.FromJson<T>(json);
        onSuccess?.Invoke(result);
    }

    // ── POST (multipart form — CSV 파일 업로드) ──────────────────

    public static IEnumerator PostFile<T>(
        string endpoint,
        byte[] fileBytes,
        string fileName,
        Action<T> onSuccess,
        Action<string> onError = null)
    {
        string url = BASE_URL + endpoint;

        WWWForm form = new WWWForm();
        form.AddBinaryData("file", fileBytes, fileName, "text/csv");

        using var req = UnityWebRequest.Post(url, form);
        req.timeout = 120;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"POST(file) {endpoint} 실패: {req.error} / {req.downloadHandler.text}";
            Debug.LogWarning(err);
            onError?.Invoke(err);
            yield break;
        }

        T result = JsonUtility.FromJson<T>(req.downloadHandler.text);
        onSuccess?.Invoke(result);
    }

    // ── 서버 연결 가능 여부 빠른 확인 ───────────────────────────

    public static IEnumerator Ping(Action<bool> callback)
    {
        string url = BASE_URL + "/api/health";
        using var req = UnityWebRequest.Get(url);
        req.timeout = 5;

        yield return req.SendWebRequest();

        callback?.Invoke(req.result == UnityWebRequest.Result.Success);
    }
}
