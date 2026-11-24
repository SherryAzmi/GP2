using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared look for the game panels: a rounded-corner sprite made in code (no texture asset needed),
/// one colour palette per game, and small helpers for building runtime UI.
/// </summary>
public static class UIStyle
{
    public enum Theme { Sea, Football }

    public struct Palette
    {
        public string title;
        public Color panel, header, edge, accent, soft;
    }

    public static Palette For(Theme theme) => theme == Theme.Football
        ? new Palette
        {
            title = "PENALTY PRACTICE",
            panel = new Color(0.03f, 0.16f, 0.08f, 1f),      // deep pitch green
            header = new Color(0.07f, 0.42f, 0.18f, 1f),
            edge = new Color(1f, 0.82f, 0.2f, 0.9f),         // gold trim
            accent = new Color(1f, 0.85f, 0.25f),
            soft = new Color(0.75f, 1f, 0.8f, 0.75f),
        }
        : new Palette
        {
            title = "BEACH WAVES",
            panel = new Color(0.02f, 0.13f, 0.24f, 1f),      // deep ocean blue
            header = new Color(0.05f, 0.45f, 0.65f, 1f),
            edge = new Color(0.98f, 0.86f, 0.6f, 0.9f),      // sand trim
            accent = new Color(1f, 0.85f, 0.25f),
            soft = new Color(0.75f, 0.92f, 1f, 0.75f),
        };

    static Sprite rounded;

    /// <summary>White rounded rectangle, 9-sliced so it stretches to any size and keeps round corners.</summary>
    public static Sprite Rounded
    {
        get
        {
            if (rounded) return rounded;
            const int size = 64, r = 24;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance outside the inner (corner-free) rectangle, antialiased over one pixel.
                    float dx = Mathf.Max(0f, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r)));
                    float dy = Mathf.Max(0f, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                    SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            rounded.name = "RoundedRect";
            return rounded;
        }
    }

    /// <summary>Gives an Image round corners; <paramref name="cornerScale"/> 1 = 24 px corners, 0.5 = 12 px.</summary>
    public static Image Round(Image img, float cornerScale = 1f)
    {
        img.sprite = Rounded;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, cornerScale);
        return img;
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static Image Box(string name, Transform parent, Vector2 pos, Vector2 size, Color color, float corner = 1f)
    {
        var img = Rect(name, parent, pos, size).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        if (corner > 0f) Round(img, corner);
        return img;
    }

    public static Text Label(string name, Transform parent, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor,
                             Color color, FontStyle style = FontStyle.Normal, string text = "")
    {
        var t = Rect(name, parent, pos, size).gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        var shadow = t.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    /// <summary>A world-space canvas floating in the scene, 1 UI pixel = <paramref name="scale"/> metres.</summary>
    public static RectTransform WorldCanvas(string name, Vector2 size, float scale, int order)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = order;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * scale;
        return rt;
    }

    /// <summary>Puts a world-space panel straight ahead of the player, level, facing them.</summary>
    public static void PlaceAhead(Transform panel, float distance, float height) => FollowAhead(panel, distance, height, 1f);

    /// <summary>
    /// Moves a panel toward the spot ahead of the player; <paramref name="blend"/> 1 snaps, small values
    /// trail the head gently (comfortable in VR and fixes panels placed before the eye height was known).
    /// </summary>
    public static void FollowAhead(Transform panel, float distance, float height, float blend)
    {
        var cam = Camera.main;
        if (!cam) return;
        Vector3 f = cam.transform.forward;
        f.y = 0f;
        f = f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized;
        Vector3 pos = cam.transform.position + f * distance + Vector3.up * height;
        Quaternion rot = Quaternion.LookRotation(f);
        panel.SetPositionAndRotation(Vector3.Lerp(panel.position, pos, blend), Quaternion.Slerp(panel.rotation, rot, blend));
    }
}
