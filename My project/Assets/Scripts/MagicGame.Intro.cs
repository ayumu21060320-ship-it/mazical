// オープニング：机の上のパソコンを 3D で映し、画面に近づいていくと、その画面（デスクトップ）がそのままゲームの画面になる
// デスクトップでは「マジカルコンパイル」のアイコンをダブルクリック（または通知の「起動する」）でゲームが始まる
// プレイヤーはプログラマー。ほかの人のパソコンに入りこんだウイルスを、このパソコンから撃退する、というメタな始まり
//
// しくみ：はじめは UI を RenderTexture に描き、3D のモニターの画面に貼る。カメラが画面いっぱいまで寄ったら、
//         UI をふつうの全画面表示に切りかえる（見た目はつながる）。クリックかキーで飛ばせる
// デスクトップは実在の OS をまねず、このゲーム用の架空のパソコン「MagiOS」の画面にしている

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public partial class MagicGame
{
    const int SceneLayer = 30;       // 3D の部屋のレイヤー（UI を描くカメラには映さない）
    RectTransform desktopLayer, toast, iconsRoot;
    GameObject sceneRoot;
    Camera uiCam;
    RenderTexture screenRT;
    Text clockText;
    float lastIconClick = -10f;
    string lastIconName;
    bool introPlaying;

    // ================= 3D のオープニング =================

    void StartIntro()
    {
        phase = Phase.Desktop;
        titleLayer.gameObject.SetActive(false);
        battleLayer.gameObject.SetActive(false);
        programScreen.SetActive(false);
        desktopLayer.gameObject.SetActive(true);
        toast.gameObject.SetActive(false);
        ambient.StartCoroutine(Intro3D());
    }

    IEnumerator Intro3D()
    {
        introPlaying = true;
        var cam = Camera.main;

        // UI はいったん RenderTexture に描く
        screenRT = new RenderTexture(1920, 1080, 24);
        uiCam = new GameObject("UICamera").AddComponent<Camera>();
        uiCam.clearFlags = CameraClearFlags.SolidColor;
        uiCam.backgroundColor = Color.black;
        uiCam.cullingMask = ~(1 << SceneLayer);
        uiCam.targetTexture = screenRT;
        uiCam.transform.position = new Vector3(0, -100, 0);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCam;
        canvas.planeDistance = 1f;

        BuildRoom();
        cam.cullingMask = 1 << SceneLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f);
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.05f;

        // 画面の真ん中と、画面がぴったり映る位置
        var screenCenter = new Vector3(0f, 1.25f, 0.53f);
        float dist = (0.855f / 2f) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var endPos = screenCenter + new Vector3(0, 0, -dist);
        var startPos = new Vector3(1.1f, 1.85f, -2.8f);

        float hold = 1.0f, move = 3.4f;
        for (float t = 0f; t < hold + move; t += Time.deltaTime)
        {
            if (AnyInput() && t > 0.2f) break;   // クリックかキーで飛ばす
            float k = Mathf.Clamp01((t - hold) / move);
            k = k * k * (3f - 2f * k);            // なめらかに
            cam.transform.position = Vector3.Lerp(startPos, endPos, k);
            cam.transform.LookAt(Vector3.Lerp(screenCenter + new Vector3(0, -0.15f, 0), screenCenter, k));
            yield return null;
        }

        // 画面いっぱいになったら、ふつうの全画面の UI に切りかえる
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        cam.cullingMask = 0;
        cam.backgroundColor = Color.black;
        Destroy(sceneRoot);
        Destroy(uiCam.gameObject);
        screenRT.Release();
        Destroy(screenRT);
        introPlaying = false;
        StartCoroutine(Noise(0.25f, 0.35f));

        // 少しして、依頼の通知が届く
        yield return new WaitForSeconds(1.2f);
        if (phase == Phase.Desktop) yield return ShowToast();
    }

    static bool AnyInput()
    {
        try { return Input.anyKeyDown; }
        catch (InvalidOperationException) { return false; }   // 入力の設定が新しい方だけのときは飛ばせない
    }

    // 机・モニター・キーボードなどを箱で作る
    void BuildRoom()
    {
        sceneRoot = new GameObject("Room");
        sceneRoot.layer = SceneLayer;
        Prim(PrimitiveType.Cube, new Vector3(0, -0.05f, 0), new Vector3(10, 0.1f, 10), new Color(0.08f, 0.07f, 0.1f));      // 床
        Prim(PrimitiveType.Cube, new Vector3(0, 2f, 1.6f), new Vector3(10, 4f, 0.1f), new Color(0.12f, 0.11f, 0.18f));      // 壁
        Prim(PrimitiveType.Cube, new Vector3(0, 0.72f, 0.6f), new Vector3(3.2f, 0.06f, 1.4f), new Color(0.38f, 0.26f, 0.18f)); // 机
        foreach (var x in new[] { -1.5f, 1.5f })
            Prim(PrimitiveType.Cube, new Vector3(x, 0.35f, 0.6f), new Vector3(0.08f, 0.7f, 1.3f), new Color(0.3f, 0.2f, 0.14f));
        // モニター
        Prim(PrimitiveType.Cube, new Vector3(0, 1.25f, 0.56f), new Vector3(1.66f, 0.99f, 0.05f), new Color(0.05f, 0.05f, 0.07f));
        Prim(PrimitiveType.Cube, new Vector3(0, 0.93f, 0.62f), new Vector3(0.08f, 0.36f, 0.06f), new Color(0.12f, 0.12f, 0.14f));
        Prim(PrimitiveType.Cube, new Vector3(0, 0.76f, 0.62f), new Vector3(0.5f, 0.02f, 0.3f), new Color(0.12f, 0.12f, 0.14f));
        var screen = Prim(PrimitiveType.Quad, new Vector3(0, 1.25f, 0.532f), new Vector3(1.52f, 0.855f, 1f), Color.white);
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
        var mat = new Material(shader) { mainTexture = screenRT };
        screen.GetComponent<Renderer>().material = mat;
        // キーボード・マウス・マグカップ・ノート
        Prim(PrimitiveType.Cube, new Vector3(0, 0.765f, 0.12f), new Vector3(0.9f, 0.03f, 0.28f), new Color(0.85f, 0.85f, 0.9f));
        Prim(PrimitiveType.Cube, new Vector3(0.65f, 0.765f, 0.12f), new Vector3(0.08f, 0.03f, 0.13f), new Color(0.85f, 0.85f, 0.9f));
        Prim(PrimitiveType.Cylinder, new Vector3(-0.95f, 0.83f, 0.25f), new Vector3(0.13f, 0.1f, 0.13f), UiKit.Hex("ff7aa8"));
        Prim(PrimitiveType.Cube, new Vector3(-1.1f, 0.765f, 0.75f), new Vector3(0.5f, 0.03f, 0.35f), UiKit.Hex("4a8fe0"));
        // 明かり：画面の青い光と、部屋の暖かい光
        AddLight("ScreenGlow", LightType.Point, new Vector3(0, 1.3f, 0.1f), new Color(0.55f, 0.7f, 1f), 2.2f, 4f);
        AddLight("Lamp", LightType.Point, new Vector3(-1.8f, 2.3f, -0.4f), new Color(1f, 0.8f, 0.6f), 1.0f, 6f);
        var sun = AddLight("Fill", LightType.Directional, Vector3.zero, new Color(0.5f, 0.55f, 0.8f), 0.25f, 0f);
        sun.transform.rotation = Quaternion.Euler(40f, -20f, 0f);
    }

    GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.layer = SceneLayer;
        go.transform.SetParent(sceneRoot.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }

    Light AddLight(string name, LightType type, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.layer = SceneLayer;
        go.transform.SetParent(sceneRoot.transform, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = type;
        l.color = color;
        l.intensity = intensity;
        if (type == LightType.Point) l.range = range;
        l.cullingMask = 1 << SceneLayer;
        return l;
    }

    // ================= デスクトップ =================

    void BuildDesktop()
    {
        desktopLayer = Layer("Desktop");
        // 壁紙（このゲーム用の、青紫に光がにじむ壁紙）
        var wall = NewRect(desktopLayer, "Wallpaper").gameObject.AddComponent<RawImage>();
        Fill(wall.rectTransform);
        wall.texture = UiKit.DiagonalGradient(UiKit.Hex("17204f"), UiKit.Hex("5b37a8"));
        wall.raycastTarget = false;
        var glows = new[] { new[] { 0.62f, 0.55f, 1300f, 0.30f, 0f }, new[] { 0.8f, 0.35f, 900f, 0.25f, 1f }, new[] { 0.45f, 0.75f, 700f, 0.2f, 2f } };
        var glowColors = new[] { UiKit.Hex("ff6fd0"), UiKit.Hex("4fd8ff"), UiKit.Hex("a07bff") };
        foreach (var g in glows)
        {
            var img = Img(desktopLayer, UiKit.Glow(), new Color(glowColors[(int)g[4]].r, glowColors[(int)g[4]].g, glowColors[(int)g[4]].b, g[3]));
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(g[0], g[1]);
            img.rectTransform.sizeDelta = new Vector2(g[2], g[2]);
        }
        var mc = Img(desktopLayer, UiKit.MagicCircle(), new Color(1, 1, 1, 0.12f)).rectTransform;
        mc.anchorMin = mc.anchorMax = new Vector2(0.64f, 0.52f);
        mc.sizeDelta = new Vector2(820, 820);
        ambient.StartCoroutine(SpinForever(mc, 3f));
        var logo = MakeText(desktopLayer, "MagiOS", 64, new Color(1, 1, 1, 0.18f), TextAnchor.MiddleCenter, true);
        Anchor(logo.rectTransform, 0.5f, 0.45f, 0.78f, 0.6f);

        // デスクトップのアイコン（左上から下へ）
        iconsRoot = NewRect(desktopLayer, "Icons");
        Anchor(iconsRoot, 0, 0, 1, 1);
        DesktopIcon("マイドキュメント", 0, IconFolder);
        DesktopIcon("PC", 1, IconMonitor);
        DesktopIcon("ごみ箱", 2, IconTrash);
        DesktopIcon("マジカルコンパイル", 3, IconGame);

        // タスクバー
        var bar = Img(desktopLayer, null, new Color(0.95f, 0.95f, 0.98f, 0.82f)).rectTransform;
        Anchor(bar, 0, 0, 1, 0, 0, 0, 0, 64);
        var start = Img(bar, UiKit.RoundRect(10), Pink);
        start.type = Image.Type.Sliced;
        Anchor(start.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, -330, -22, -286, 22);
        var startMark = MakeText(start.rectTransform, "M", 30, Color.white, TextAnchor.MiddleCenter, true);
        startMark.fontStyle = FontStyle.BoldAndItalic;
        var search = Img(bar, UiKit.RoundRect(18), new Color(1, 1, 1, 0.95f));
        search.type = Image.Type.Sliced;
        Anchor(search.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, -276, -20, -70, 20);
        var st = MakeText(search.rectTransform, "　検索", 22, new Color(0.4f, 0.4f, 0.45f), TextAnchor.MiddleLeft, false);
        Fill(st.rectTransform);
        var pins = new Action<RectTransform>[] { IconFolder, IconMonitor, IconGame };
        for (int i = 0; i < pins.Length; i++)
        {
            var slot = NewRect(bar, "Pin");
            Anchor(slot, 0.5f, 0.5f, 0.5f, 0.5f, -40 + i * 56, -22, 4 + i * 56, 22);
            pins[i](slot);
        }
        var tray = MakeText(bar, "▲　あ　Wi-Fi", 22, new Color(0.25f, 0.25f, 0.3f), TextAnchor.MiddleRight, false);
        Anchor(tray.rectTransform, 1, 0, 1, 1, -360, 0, -190, 0);
        clockText = MakeText(bar, "", 20, new Color(0.15f, 0.15f, 0.2f), TextAnchor.MiddleRight, false);
        Anchor(clockText.rectTransform, 1, 0, 1, 1, -180, 0, -24, 0);

        // 依頼の通知（右下）
        toast = Img(desktopLayer, UiKit.RoundRect(16), new Color(0.98f, 0.98f, 1f, 0.97f)).rectTransform;
        toast.GetComponent<Image>().type = Image.Type.Sliced;
        toast.anchorMin = toast.anchorMax = new Vector2(1, 0);
        toast.pivot = new Vector2(1, 0);
        toast.sizeDelta = new Vector2(520, 230);
        toast.anchoredPosition = new Vector2(-20, 80);
        var head = MakeText(toast, "リモートサポートの依頼", 26, Ink, TextAnchor.UpperLeft, true);
        Anchor(head.rectTransform, 0, 1, 1, 1, 24, -56, -20, -16);
        var body = MakeText(toast, "友だちのパソコンがウイルスに感染したみたい！\n『マジカルコンパイル』でつないで、助けてあげて！", 22, Ink, TextAnchor.UpperLeft, false);
        Anchor(body.rectTransform, 0, 0, 1, 1, 24, 70, -20, -60);
        var open = CommandButton(toast, "起動する", 28, OpenGame, true);
        Anchor((RectTransform)open.transform, 0.45f, 0, 0.95f, 0, 0, 18, 0, 70);
        toast.gameObject.SetActive(false);

        desktopLayer.gameObject.SetActive(false);
    }

    IEnumerator ShowToast()
    {
        toast.gameObject.SetActive(true);
        var end = toast.anchoredPosition;
        var from = end + new Vector2(560, 0);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            float k = 1f - (1f - t) * (1f - t);
            toast.anchoredPosition = Vector2.Lerp(from, end, k);
            yield return null;
        }
        toast.anchoredPosition = end;
    }

    // アイコン 1 つ（ダブルクリックで開く）
    void DesktopIcon(string label, int row, Action<RectTransform> drawIcon)
    {
        var cell = Img(iconsRoot, UiKit.RoundRect(10), new Color(1, 1, 1, 0f));
        cell.type = Image.Type.Sliced;
        cell.raycastTarget = true;
        var rt = cell.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(140, 150);
        rt.anchoredPosition = new Vector2(16, -16 - row * 160);
        var art = NewRect(rt, "Icon");
        Anchor(art, 0.5f, 1, 0.5f, 1, -42, -98, 42, -14);
        drawIcon(art);
        var t = MakeText(rt, label, 20, Color.white, TextAnchor.UpperCenter, false);
        Anchor(t.rectTransform, 0, 0, 1, 0, 0, 0, 0, 48);
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.8f);
        sh.effectDistance = new Vector2(2, -2);
        var b = rt.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => OnIconClicked(label, cell));
    }

    void OnIconClicked(string label, Image cell)
    {
        foreach (Transform c in iconsRoot) c.GetComponent<Image>().color = new Color(1, 1, 1, 0f);
        cell.color = new Color(0.6f, 0.8f, 1f, 0.35f);   // 選ばれたアイコン
        bool dbl = lastIconName == label && Time.time - lastIconClick < 0.45f;
        lastIconClick = Time.time;
        lastIconName = label;
        if (!dbl) return;
        if (label == "マジカルコンパイル") OpenGame();
        else StartCoroutine(AccessDenied(label));
    }

    // ほかのアイコンは開けない（ウイルスのしわざ？）
    IEnumerator AccessDenied(string label)
    {
        var box = Img(desktopLayer, UiKit.RoundRect(16), new Color(0.98f, 0.95f, 0.97f, 0.97f));
        box.type = Image.Type.Sliced;
        Anchor(box.rectTransform, 0.36f, 0.42f, 0.64f, 0.58f);
        MakeText(box.rectTransform, "「" + label + "」を開けません\nウイルスのしわざかも…？", 26, UiKit.Hex("c0103a"), TextAnchor.MiddleCenter, true);
        StartCoroutine(Noise(0.25f, 0.35f));
        yield return new WaitForSeconds(1.4f);
        Destroy(box.gameObject);
    }

    // ゲームのウィンドウが開いて、タイトル画面へ
    void OpenGame()
    {
        if (phase != Phase.Desktop || introPlaying) return;
        StartCoroutine(OpenWindow());
    }

    IEnumerator OpenWindow()
    {
        phase = Phase.Title;
        var win = Img(desktopLayer, null, UiKit.Hex("ffd3ec")).rectTransform;
        win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
        var bar = Img(win, null, Pink).rectTransform;
        Anchor(bar, 0, 1, 1, 1, 0, -36, 0, 0);
        MakeText(bar, "　マジカルコンパイル", 20, Color.white, TextAnchor.MiddleLeft, true);
        MakeText(bar, "ー　□　×　", 20, Color.white, TextAnchor.MiddleRight, true);
        var hat = Img(win, UiKit.Art("hat"), Color.white).rectTransform;
        hat.sizeDelta = UiKit.ArtSize("hat") * 3f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            float k = t * t * (3f - 2f * t);
            win.sizeDelta = Vector2.Lerp(new Vector2(160, 120), new Vector2(1920, 1080), k);
            yield return null;
        }
        win.sizeDelta = new Vector2(1920, 1080);
        yield return new WaitForSeconds(0.25f);
        Destroy(win.gameObject);
        desktopLayer.gameObject.SetActive(false);
        ShowTitle();
    }

    void UpdateClock()
    {
        if (clockText == null || !desktopLayer.gameObject.activeSelf) return;
        var now = DateTime.Now;
        clockText.text = now.ToString("H:mm") + "\n" + now.ToString("yyyy/MM/dd");
    }

    // ---- アイコンの絵（四角と丸で描く） ----

    void IconFolder(RectTransform r)
    {
        var tab = Img(r, UiKit.RoundRect(6), UiKit.Hex("e8a820"));
        tab.type = Image.Type.Sliced;
        Anchor(tab.rectTransform, 0.05f, 0.6f, 0.45f, 0.8f);
        var body = Img(r, UiKit.RoundRect(8), UiKit.Hex("ffc94a"));
        body.type = Image.Type.Sliced;
        Anchor(body.rectTransform, 0.05f, 0.12f, 0.95f, 0.72f);
    }

    void IconMonitor(RectTransform r)
    {
        var frame = Img(r, UiKit.RoundRect(6), UiKit.Hex("2b2b33"));
        frame.type = Image.Type.Sliced;
        Anchor(frame.rectTransform, 0.05f, 0.3f, 0.95f, 0.9f);
        var screen = Img(frame.rectTransform, null, UiKit.Hex("4fb8ff"));
        Anchor(screen.rectTransform, 0, 0, 1, 1, 5, 5, -5, -5);
        var stand = Img(r, null, UiKit.Hex("2b2b33"));
        Anchor(stand.rectTransform, 0.42f, 0.14f, 0.58f, 0.3f);
        var baseImg = Img(r, null, UiKit.Hex("2b2b33"));
        Anchor(baseImg.rectTransform, 0.28f, 0.1f, 0.72f, 0.15f);
    }

    void IconTrash(RectTransform r)
    {
        var lid = Img(r, null, UiKit.Hex("9aa0b4"));
        Anchor(lid.rectTransform, 0.18f, 0.78f, 0.82f, 0.86f);
        var body = Img(r, UiKit.RoundRect(6), UiKit.Hex("c3c8d8"));
        body.type = Image.Type.Sliced;
        Anchor(body.rectTransform, 0.24f, 0.12f, 0.76f, 0.76f);
        for (int i = 0; i < 3; i++)
        {
            var line = Img(body.rectTransform, null, UiKit.Hex("9aa0b4"));
            Anchor(line.rectTransform, 0.25f + i * 0.22f, 0.15f, 0.31f + i * 0.22f, 0.85f);
        }
    }

    void IconGame(RectTransform r)
    {
        var bg = Img(r, UiKit.RoundRect(12), Pink);
        bg.type = Image.Type.Sliced;
        Anchor(bg.rectTransform, 0.08f, 0.08f, 0.92f, 0.92f);
        var hat = Img(bg.rectTransform, UiKit.Art("hat"), Color.white);
        hat.preserveAspect = true;
        Anchor(hat.rectTransform, 0.08f, 0.08f, 0.92f, 0.92f);
    }
}
