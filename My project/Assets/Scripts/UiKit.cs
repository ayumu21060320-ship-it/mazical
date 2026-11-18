// 画面の部品の形をコードで作る（画像ファイルを使わない）
//   角の丸い四角・斜めの帯（コマンド）・ブロック（上にへこみ、下に出っぱり）・丸・魔法陣・グラデーション・床の方眼
// どれも白で作り、Image の色で色を付ける

using System.Collections.Generic;
using UnityEngine;

public static class UiKit
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    static Texture2D NewTex(int w, int h, bool point = false)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    // ふちを少しぼかした塗り（0〜1）。d は形の内側までの距離（内側が正）
    static float Edge(float d) => Mathf.Clamp01(d + 0.5f);

    // 角の丸い四角（9 スライスで伸ばしても角が丸いまま）
    public static Sprite RoundRect(int radius)
    {
        string key = "round" + radius;
        if (cache.TryGetValue(key, out var sp)) return sp;
        int size = radius * 2 + 4;
        var t = NewTex(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = radius - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                t.SetPixel(x, y, new Color(1, 1, 1, Edge(d)));
            }
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        cache[key] = sp;
        return sp;
    }

    // 上だけ角の丸い四角（フォルダのタブ）
    public static Sprite TabShape()
    {
        if (cache.TryGetValue("tab", out var sp)) return sp;
        int r = 14, size = 40;
        var t = NewTex(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                float cy = Mathf.Min(y + 0.5f, size - r);
                float d = r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                t.SetPixel(x, y, new Color(1, 1, 1, Edge(d)));
            }
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, 2, r, r));
        cache["tab"] = sp;
        return sp;
    }

    // 斜めの帯（左下から右上へ傾いた平行四辺形。右端は少しとがる）。コマンドやターン表示に使う
    public static Sprite Slant()
    {
        if (cache.TryGetValue("slant", out var sp)) return sp;
        int w = 128, h = 32;
        var t = NewTex(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fy = (y + 0.5f) / h;
                float left = 10f * (1f - fy);             // 左は斜め
                float right = w - 10f * fy;               // 右も斜め
                float d = Mathf.Min(x + 0.5f - left, right - (x + 0.5f));
                d = Mathf.Min(d, Mathf.Min(y + 0.5f, h - y - 0.5f));
                t.SetPixel(x, y, new Color(1, 1, 1, Edge(d)));
            }
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(14, 2, 14, 2));
        cache["slant"] = sp;
        return sp;
    }

    // ターン表示の帯（右が矢印のようにとがる）
    public static Sprite Arrow()
    {
        if (cache.TryGetValue("arrow", out var sp)) return sp;
        int w = 128, h = 48;
        var t = NewTex(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fy = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;   // 中央 0、上下 1
                float right = w - 20f * fy;
                float d = Mathf.Min(right - (x + 0.5f), Mathf.Min(y + 0.5f, h - y - 0.5f));
                t.SetPixel(x, y, new Color(1, 1, 1, Edge(d)));
            }
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 24, 2));
        cache["arrow"] = sp;
        return sp;
    }

    // プログラムのブロック（上にへこみ、下に出っぱり。角は少し丸い）
    // 高さ 56 のうち、下の 8 ドットが出っぱり用
    public const float BlockTabHeight = 8f;
    public static Sprite BlockShape()
    {
        if (cache.TryGetValue("block", out var sp)) return sp;
        int w = 128, h = 56, body = 48, r = 6;
        var t = NewTex(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = h - y - 0.5f;     // py は上から
                float a = 0f;
                if (py < body)
                {
                    float cx = Mathf.Clamp(px, r, w - r), cy = Mathf.Clamp(py, r, body - r);
                    float d = r - Vector2.Distance(new Vector2(px, cy), new Vector2(cx, cy)) ;
                    d = Mathf.Min(r - Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)), d);
                    a = Edge(d);
                    if (px > 18 && px < 38 && py < 6) a = 0f;  // 上のへこみ
                }
                else if (px > 19 && px < 37 && py < body + 6) a = 1f; // 下の出っぱり
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(42, 10, 10, 8));
        cache["block"] = sp;
        return sp;
    }

    // スタートのブロック（上がふくらんだ帽子の形）
    public static Sprite HatBlockShape()
    {
        if (cache.TryGetValue("hatblock", out var sp)) return sp;
        int w = 128, h = 72, top = 16, body = 64, r = 6;
        var t = NewTex(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = h - y - 0.5f;
                float a = 0f;
                if (py < top)
                {
                    float d = 1f - Vector2.Distance(new Vector2(px / 52f, (py - top) / top), Vector2.zero);
                    a = px < 104 ? Edge(d * 20f) : 0f;
                }
                else if (py < body)
                {
                    float cx = Mathf.Clamp(px, r, w - r), cy = Mathf.Clamp(py, top, body - r);
                    a = Edge(r - Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)));
                }
                else if (px > 19 && px < 37 && py < body + 6) a = 1f;
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(42, 10, 26, 18));
        cache["hatblock"] = sp;
        return sp;
    }

    public static Sprite Circle()
    {
        if (cache.TryGetValue("circle", out var sp)) return sp;
        int s = 64;
        var t = NewTex(s, s);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                t.SetPixel(x, y, new Color(1, 1, 1, Edge(s / 2f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)))));
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        cache["circle"] = sp;
        return sp;
    }

    // ぼんやりした丸（影・光）
    public static Sprite Glow()
    {
        if (cache.TryGetValue("glow", out var sp)) return sp;
        int s = 64;
        var t = NewTex(s, s);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                float a = Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        cache["glow"] = sp;
        return sp;
    }

    // 魔法陣（白い線。円・六芒星・小さな点の文字）
    public static Sprite MagicCircle()
    {
        if (cache.TryGetValue("magic", out var sp)) return sp;
        int s = 256;
        var t = NewTex(s, s);
        var px = new float[s, s];
        var c = new Vector2(s / 2f, s / 2f);
        Ring(px, s, 124, 2.5f); Ring(px, s, 112, 1.5f); Ring(px, s, 78, 1.5f); Ring(px, s, 20, 1.5f);
        for (int k = 0; k < 2; k++)
            for (int i = 0; i < 3; i++)
            {
                float a1 = (i * 120 + k * 60 + 90) * Mathf.Deg2Rad, a2 = ((i + 1) * 120 + k * 60 + 90) * Mathf.Deg2Rad;
                Line(px, s, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 110, c + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * 110, 1.4f);
            }
        for (int i = 0; i < 36; i++)   // 円のあいだの点（文字のかわり）
        {
            float a = i * 10 * Mathf.Deg2Rad;
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 118;
            float len = (i % 3 == 0) ? 4f : 2f;
            Line(px, s, p - new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len, 1.2f);
        }
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                t.SetPixel(x, y, new Color(1, 1, 1, px[x, y]));
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        cache["magic"] = sp;
        return sp;
    }

    static void Ring(float[,] px, int s, float r, float w)
    {
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) - r);
                px[x, y] = Mathf.Max(px[x, y], Mathf.Clamp01(w - d));
            }
    }

    static void Line(float[,] px, int s, Vector2 a, Vector2 b, float w)
    {
        int n = (int)(Vector2.Distance(a, b) * 2);
        for (int i = 0; i <= n; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)n);
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = (int)p.x + dx, y = (int)p.y + dy;
                    if (x < 0 || y < 0 || x >= s || y >= s) continue;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), p);
                    px[x, y] = Mathf.Max(px[x, y], Mathf.Clamp01(w - d));
                }
        }
    }

    // 縦のグラデーション（下 bottom → 上 top）
    public static Texture2D VerticalGradient(Color bottom, Color top)
    {
        var t = NewTex(2, 64);
        for (int y = 0; y < 64; y++)
        {
            var c = Color.Lerp(bottom, top, y / 63f);
            t.SetPixel(0, y, c);
            t.SetPixel(1, y, c);
        }
        t.Apply();
        return t;
    }

    // 斜めのグラデーション（左下 a → 右上 b）
    public static Texture2D DiagonalGradient(Color a, Color b)
    {
        var t = NewTex(64, 64);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                t.SetPixel(x, y, Color.Lerp(a, b, (x + y) / 126f));
        t.Apply();
        return t;
    }

    // 奥へ続く床の方眼（パソコンの中の床）。下が手前
    public static Texture2D FloorGrid(Color bg, Color line)
    {
        int w = 512, h = 256;
        var t = NewTex(w, h);
        for (int y = 0; y < h; y++)
        {
            float depth = (y + 1f) / h;                    // 0 = 手前, 1 = 奥
            float z = 1f / (0.15f + depth * 2.2f);          // 奥ほど細かく
            float rowLine = Mathf.Abs(Mathf.Repeat(z * 6f, 1f) - 0.5f);
            for (int x = 0; x < w; x++)
            {
                float fx = (x + 0.5f) / w - 0.5f;
                float colLine = Mathf.Abs(Mathf.Repeat(fx * 14f / (0.3f + depth * 1.4f), 1f) - 0.5f);
                float l = Mathf.Max(Mathf.Clamp01((0.06f - (0.5f - rowLine)) * 30f), Mathf.Clamp01((0.05f - (0.5f - colLine)) * 30f));
                var c = Color.Lerp(bg, line, l * (1f - depth * 0.7f));
                c.a = 1f;
                t.SetPixel(x, y, c);
            }
        }
        t.Apply();
        return t;
    }

    // ドット絵（ArtData.cs の PNG）から絵を作る。足もと（下の真ん中）が基準。crop は上から数えた範囲
    public static Sprite Art(string name, RectInt? crop = null)
    {
        string key = "art:" + name + (crop.HasValue ? crop.Value.ToString() : "");
        if (cache.TryGetValue(key, out var sp)) return sp;
        var t = ArtTexture(name);
        Rect rect = new Rect(0, 0, t.width, t.height);
        if (crop.HasValue)
        {
            var cr = crop.Value;
            rect = new Rect(cr.x, t.height - cr.y - cr.height, cr.width, cr.height);
        }
        sp = Sprite.Create(t, rect, new Vector2(0.5f, 0f), 1f);
        cache[key] = sp;
        return sp;
    }

    static readonly Dictionary<string, Texture2D> artTex = new Dictionary<string, Texture2D>();
    public static Texture2D ArtTexture(string name)
    {
        if (artTex.TryGetValue(name, out var t)) return t;
        t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(System.Convert.FromBase64String(ArtData.Png[name]));
        t.filterMode = FilterMode.Point;   // ドットをぼかさない
        t.wrapMode = TextureWrapMode.Clamp;
        artTex[name] = t;
        return t;
    }

    public static Vector2 ArtSize(string name)
    {
        var t = ArtTexture(name);
        return new Vector2(t.width, t.height);
    }

    public static Color Hex(string s)
    {
        int v = System.Convert.ToInt32(s.TrimStart('#'), 16);
        return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }

    // 砂嵐（白黒のランダムな点）
    public static Texture2D Static()
    {
        var tex = NewTex(160, 90, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < 90; y++)
            for (int x = 0; x < 160; x++)
            {
                float v = Random.value;
                tex.SetPixel(x, y, new Color(v, v, v, Random.value < 0.5f ? 0f : 1f));
            }
        tex.Apply();
        return tex;
    }

    // うっすら走査線（4 ドットに 1 本）
    public static Texture2D Scanlines()
    {
        var tex = NewTex(1, 4, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < 4; y++) tex.SetPixel(0, y, new Color(0, 0, 0, y == 0 ? 0.12f : 0f));
        tex.Apply();
        return tex;
    }
}
