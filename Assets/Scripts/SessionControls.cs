using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Therapist's PAUSE and STOP buttons on the PC monitor while a session is running (not visible in the headset).
/// Pause freezes game time, so holds and time limits stop counting. Stop saves what was done.
/// </summary>
public class SessionControls : MonoBehaviour
{
    public static bool Paused { get; private set; }

    GameObject root;
    Text pauseLabel;
    Action onStop;

    public static SessionControls Create(Transform owner, Action stop)
    {
        if (!FindFirstObjectByType<EventSystem>())
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var go = new GameObject("SessionControls");
        go.transform.SetParent(owner, false);
        var controls = go.AddComponent<SessionControls>();
        controls.onStop = stop;
        controls.Build();
        return controls;
    }

    public void SetVisible(bool visible)
    {
        if (!visible) SetPaused(false);
        root.SetActive(visible);
    }

    void SetPaused(bool value)
    {
        if (Paused == value) return;
        Paused = value;
        Time.timeScale = value ? 0f : 1f;
        AudioListener.pause = value;
        pauseLabel.text = value ? "RESUME" : "PAUSE";
    }

    // Never leave the next scene frozen.
    void OnDestroy()
    {
        if (!Paused) return;
        Paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    void Build()
    {
        root = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        DoctorPanel.ConfigureCanvas(root, false, 90);
        var rt = (RectTransform)root.transform;
        pauseLabel = MakeButton(rt, "PAUSE", new Vector2(-460f, 70f), new Color(0.16f, 0.6f, 0.95f, 0.95f), () => SetPaused(!Paused));
        MakeButton(rt, "STOP & SAVE", new Vector2(-180f, 70f), new Color(0.85f, 0.3f, 0.25f, 0.95f), () =>
        {
            SetPaused(false);
            onStop?.Invoke();
        });
        root.SetActive(false);
    }

    /// <summary>A button anchored to the bottom-right corner of the monitor.</summary>
    static Text MakeButton(RectTransform parent, string label, Vector2 fromCorner, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var rt = DoctorPanel.Child("Button_" + label, parent, color);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.anchoredPosition = fromCorner;
        rt.sizeDelta = new Vector2(260f, 76f);
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = rt.GetComponent<Image>();
        button.onClick.AddListener(onClick);
        return DoctorPanel.Label(rt, label, Vector2.zero, new Vector2(260f, 76f), 28, TextAnchor.MiddleCenter, Color.white);
    }
}
