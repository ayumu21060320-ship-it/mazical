// マジカルコンパイルの画面と操作。シーンを再生すると自動で起動する（シーンに何も置かなくてよい）
// 世界観: プレイヤーはプログラマー。パソコンの中でプログラムを組んで魔法を作り、AI ロボットに送ってウイルスを撃退する
//
//   タイトル画面:   ロゴと3人。「スタート」で戦闘へ
//   戦闘画面:       味方（左）とウイルス（右）が舞台に立つ。味方をクリックで選び、右のコマンドから
//                   「プログラム」で魔法を組む。「バトル開始」でタイムラインが進み、右端に着いたキャラから行動する
//                   ウイルスをクリックすると「1体」の魔法でねらう敵を選べる
//   プログラム画面: 左の手持ちブロックをドラッグして中央のコードに置く。コードの外へドラッグすると消える
//                   ブロックの ▼ の部分をクリックすると中身が変わる。「送信」でロボットに送る
//
// 絵はすべてドット絵（ArtData.cs）。部品の形は UiKit.cs がコードで作る

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MagicBattle;

public class MagicGame : MonoBehaviour
{
    // ---- 色（企画書に合わせる）----
    static readonly Color Pink = UiKit.Hex("ff4fb8");
    static readonly Color Purple = UiKit.Hex("5a45d8");
    static readonly Color Cyan = UiKit.Hex("3ff0ff");
    static readonly Color Magenta = UiKit.Hex("ff4fd8");
    static readonly Color CommandBg = new Color(0.07f, 0.08f, 0.11f, 0.94f);
    static readonly Color CommandEdge = UiKit.Hex("3cd8d0");
    static readonly Color Ink = UiKit.Hex("2a1e3a");
    // ブロックの色（属性=青, 対象=紫, 威力=緑, 発射方法=ピンク, 追加効果=黄土）
    static readonly Color[] BlockColors =
    {
        UiKit.Hex("4a8fe0"), UiKit.Hex("7c5cc9"), UiKit.Hex("2fae4a"), UiKit.Hex("e0408a"), UiKit.Hex("a39b3a"),
    };
    static readonly Color StartColor = UiKit.Hex("f2b632");

    const float AllyScale = 6f;        // 戦闘画面のドット 1 つの大きさ
    static readonly RectInt FaceCrop = new RectInt(5, 1, 30, 27);   // 顔だけ切り出す範囲（上から数える）

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<MagicGame>() == null) new GameObject("MagicGame").AddComponent<MagicGame>();
    }

    enum Phase { Title, Planning, Running, Finished }

    Battle battle;
    Phase phase = Phase.Title;
    readonly Dictionary<Unit, List<Block>> drafts = new Dictionary<Unit, List<Block>>(); // 組みかけのコード
    Unit selected;         // 選んでいる味方（コマンドの持ち主）
    Unit editing;          // プログラム画面で組んでいる味方
    bool busy;             // 行動のアニメーション中はタイムラインを止める
    int flying;            // 飛んでいる魔法の弾の数
    bool intro;            // ウイルスの登場演出中
    const float TimelineSpeed = 1.0f;

    Font font;
    Canvas canvas;
    RectTransform root;
    RectTransform titleLayer, battleLayer, stage, effects, noiseLayer;
    RawImage noise;
    CoroutineRunner ambient;   // 背景の演出（ずっと動かすもの）

    // 戦闘画面
    class UnitView
    {
        public Unit U;
        public RectTransform Root;      // 足もとの位置
        public RectTransform Art;       // 絵（足もとが基準）
        public Image ArtImage;
        public Sprite Idle, Blink, Cast, Hurt;
        public RectTransform Ring;      // 足もとの魔法陣（選択中・ねらう敵）
        public RectTransform HpBox;     // 敵の HP の表示
        public Text HpText;
        public Image HpFill;
        public Vector2 Size;
        public Vector2 Offset;
        public float Tilt;
        public float Phase;
        public float BlinkTimer;
        public float PoseTimer;         // 魔法・ダメージのポーズを続ける時間
        public float GlitchTimer;
        public int ShownHp;
        public bool Dying, Dead;
    }
    readonly Dictionary<Unit, UnitView> views = new Dictionary<Unit, UnitView>();
    readonly Dictionary<Unit, (Text hp, Image fill, RectTransform box)> allyHud = new Dictionary<Unit, (Text, Image, RectTransform)>();
    readonly Dictionary<Unit, RectTransform> icons = new Dictionary<Unit, RectTransform>();
    Text turnText, logText, commandTitle;
    RectTransform commandMenu, timelineBar, pointerLine, hudPanel;
    readonly List<Button> commandButtons = new List<Button>();
    GameObject resultPanel;
    Text resultText;
    readonly List<string> log = new List<string>();

    // プログラム画面
    GameObject programScreen;
    RectTransform codeArea, codeList, paletteList;
    Text memoryText, speedText, previewText, programMessage, bubbleText, robotName;
    Image memoryFill, robotPortrait;
    readonly List<(Unit unit, Image bg, Text text, Image face)> robotTabs = new List<(Unit, Image, Text, Image)>();

    // ================= 起動 =================

    void Start()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        // UI の文字のフォント。Assets/Resources/UIFont/ に日本語フォントを置けばそれを使う。無ければ PC のフォント
        var uiFonts = Resources.LoadAll<Font>("UIFont");
        font = uiFonts.Length > 0 ? uiFonts[0]
             : Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic" }, 64);

        ambient = new GameObject("Ambient").AddComponent<CoroutineRunner>();
        BuildCanvas();
        BuildTitle();
        BuildBattle();
        effects = Layer("Effects");
        BuildProgramScreen();
        BuildNoise();
        ShowTitle();
    }

    void ShowTitle()
    {
        StopAllCoroutines();
        phase = Phase.Title;
        titleLayer.gameObject.SetActive(true);
        battleLayer.gameObject.SetActive(false);
        programScreen.SetActive(false);
        ClearChildren(effects);
        StartCoroutine(Noise(0.4f, 0.5f));
    }

    void OnTitleStart()
    {
        if (phase != Phase.Title) return;
        titleLayer.gameObject.SetActive(false);
        battleLayer.gameObject.SetActive(true);
        NewBattle();
    }

    void NewBattle()
    {
        StopAllCoroutines();
        ClearChildren(effects);
        for (int i = noiseLayer.childCount - 1; i >= 2; i--) Destroy(noiseLayer.GetChild(i).gameObject); // 走査線と砂嵐は残す
        noise.color = new Color(1, 1, 1, 0);
        busy = false;
        flying = 0;
        battle = new Battle(System.Environment.TickCount);
        drafts.Clear();
        foreach (var a in battle.Allies) drafts[a] = new List<Block>();
        selected = editing = battle.Allies[0];
        for (int i = 0; i < robotTabs.Count; i++)
        {
            var t = robotTabs[i];
            robotTabs[i] = (battle.Allies[i], t.bg, t.text, t.face);
            t.face.sprite = UiKit.Art(battle.Allies[i].Art + "_idle", FaceCrop);
        }
        phase = Phase.Planning;
        log.Clear();
        AddLog("パソコンにウイルスが侵入！ 味方を選んで「プログラム」で魔法を組もう");
        BuildUnits();
        programScreen.SetActive(false);
        resultPanel.SetActive(false);
        RefreshBattle();
        StartCoroutine(Intro());
    }

    // ================= 毎フレーム =================

    void Update()
    {
        if (phase == Phase.Running && !busy) StepBattle(Time.deltaTime);
        if (battle != null && battleLayer.gameObject.activeSelf)
        {
            UpdateTimeline();
            AnimateUnits();
            UpdatePointerLine();
        }
    }

    void StepBattle(float dt)
    {
        var ready = battle.NextReady();
        if (ready != null)
        {
            var r = battle.Act(ready);
            if (!string.IsNullOrEmpty(r.Text)) AddLog(r.Text);
            StartCoroutine(PlayAction(r));
            return;
        }
        if (battle.TurnOver)
        {
            battle.EndTurn();
            phase = Phase.Planning;
            AddLog("ターン" + battle.Turn + "。プログラムを組み直すか、そのまま「バトル開始」");
            RefreshBattle();
            return;
        }
        battle.Advance(dt * TimelineSpeed);
    }

    void UpdateTimeline()
    {
        float w = timelineBar.rect.width;
        foreach (var kv in icons)
        {
            var u = kv.Key;
            kv.Value.gameObject.SetActive(views.TryGetValue(u, out var v) ? !v.Dead : u.Alive);
            float t = Mathf.Clamp01(u.Gauge / Battle.GaugeLength);
            var p = kv.Value.anchoredPosition;
            p.x = t * w;
            kv.Value.anchoredPosition = p;
        }
    }

    // 選んでいる味方からコマンドへ、ピンクの線を引く
    void UpdatePointerLine()
    {
        bool show = phase == Phase.Planning && !intro && selected != null && views.ContainsKey(selected) && !programScreen.activeSelf;
        pointerLine.gameObject.SetActive(show);
        if (!show) return;
        var v = views[selected];
        Vector2 a = root.InverseTransformPoint(v.Art.TransformPoint(new Vector3(0, v.Size.y * 0.62f, 0)));
        Vector2 b = root.InverseTransformPoint(commandMenu.TransformPoint(new Vector3(commandMenu.rect.xMin, commandMenu.rect.center.y, 0)));
        pointerLine.anchoredPosition = (a + b) / 2f;
        pointerLine.sizeDelta = new Vector2(Vector2.Distance(a, b), 4f);
        pointerLine.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
    }

    // ================= ボタン =================

    void OnAllyClicked(Unit u)
    {
        if (phase != Phase.Planning || intro || !u.Alive) return;
        selected = u;
        RefreshBattle();
    }

    void OnEnemyClicked(Unit e)
    {
        if (!e.Alive) return;
        battle.TargetIndex = battle.Enemies.IndexOf(e);
        RefreshBattle();
    }

    void OnProgramCommand()
    {
        if (phase != Phase.Planning || intro) return;
        if (selected == null || !selected.Alive) selected = battle.Allies.Find(a => a.Alive);
        editing = selected;
        programScreen.SetActive(true);
        RefreshProgram();
        StartCoroutine(Noise(0.2f, 0.3f));
    }

    void OnNotReady(string name)
    {
        AddLog(name + "はまだ準備中です");
    }

    void OnStartButton()
    {
        if (phase != Phase.Planning || intro) return;
        programScreen.SetActive(false);
        phase = Phase.Running;
        var none = battle.Allies.FindAll(a => a.Alive && a.Spell == null);
        if (none.Count > 0) AddLog("プログラムを送信していないロボットは何もしません");
        RefreshBattle();
    }

    void OnSend()
    {
        var code = drafts[editing];
        if (!Compiler.Check(code, editing.Capacity, out string msg))
        {
            programMessage.text = "送信できません：" + msg;
            programMessage.color = UiKit.Hex("e0284a");
            StartCoroutine(Shake(programMessage.rectTransform));
            return;
        }
        var spell = Compiler.Build(code);
        battle.SetSpell(editing, spell);
        programMessage.text = "送信しました！ " + editing.Name + "：" + spell.Describe();
        programMessage.color = UiKit.Hex("1e9a4a");
        AddLog(editing.Name + "にプログラムを送信（" + spell.Describe() + "）");
        RefreshBattle();
        RefreshProgram(keepMessage: true);
        StartCoroutine(Noise(0.15f, 0.25f));
    }

    // ================= ブロックのドラッグ（BlockView から呼ばれる） =================

    public void OnBlockDropped(BlockView view, Vector2 screenPos)
    {
        var code = drafts[editing];
        bool overCode = RectTransformUtility.RectangleContainsScreenPoint(codeArea, screenPos, null);
        int from = view.FromPalette ? -1 : code.IndexOf(view.Data);
        if (from >= 0) code.RemoveAt(from);
        if (overCode)
        {
            int index = code.Count;
            int childIndex = 0;
            for (int i = 0; i < codeList.childCount; i++)
            {
                var child = (RectTransform)codeList.GetChild(i);
                var bv = child.GetComponent<BlockView>();
                if (bv == null || bv == view) continue;
                if (screenPos.y > child.position.y) { index = childIndex; break; }
                childIndex++;
            }
            code.Insert(Mathf.Min(index, code.Count), view.FromPalette ? view.Data.Clone() : view.Data);
        }
        RefreshProgram();
    }

    public void OnBlockValueClicked(BlockView view)
    {
        view.Data.Value = (view.Data.Value + 1) % Blocks.OptionCount(view.Data.Kind);
        view.RefreshLabels();
        if (!view.FromPalette) RefreshProgram();
    }

    // ================= 表示の更新 =================

    void RefreshBattle()
    {
        turnText.text = "ターン" + battle.Turn;
        var target = battle.CurrentTarget();
        foreach (var kv in views)
        {
            var u = kv.Key;
            var v = kv.Value;
            if (u.IsAlly)
            {
                bool sel = u == selected && phase == Phase.Planning;
                v.Ring.gameObject.SetActive(sel && !v.Dead);
                var hud = allyHud[u];
                hud.hp.text = "HP" + v.ShownHp;
                hud.fill.fillAmount = (float)v.ShownHp / u.MaxHp;
                hud.box.GetComponent<CanvasGroup>().alpha = v.ShownHp > 0 ? 1f : 0.4f;
            }
            else
            {
                v.Ring.gameObject.SetActive(u == target && !v.Dead);
                v.HpText.text = "HP" + v.ShownHp;
                v.HpFill.fillAmount = (float)v.ShownHp / u.MaxHp;
                v.HpBox.gameObject.SetActive(!v.Dead && !intro);
            }
        }

        bool plan = phase == Phase.Planning && !intro;
        commandMenu.gameObject.SetActive(plan);
        if (selected != null)
            commandTitle.text = selected.Name + "　<size=22>" + selected.TraitName() + "</size>\n<size=22>"
                + (selected.Spell == null ? "プログラム：まだ無い" : selected.Spell.Describe() + "（" + Compiler.SpeedLabel(selected.Spell.Memory, selected.Capacity) + "）") + "</size>";
        foreach (var b in commandButtons) b.interactable = plan;
    }

    void RefreshProgram(bool keepMessage = false)
    {
        var code = drafts[editing];
        foreach (var t in robotTabs)
        {
            bool sel = t.unit == editing;
            t.bg.color = sel ? Pink : new Color(1, 1, 1, 0.85f);
            t.text.color = sel ? Color.white : Ink;
            t.text.text = t.unit.Name + (t.unit.Spell != null ? "\n<size=18>送信済み</size>" : "") + (t.unit.Alive ? "" : "\n<size=18>戦闘不能</size>");
        }
        robotName.text = editing.Name;
        robotPortrait.sprite = UiKit.Art(editing.Art + "_idle", new RectInt(0, 0, 40, 44));
        bubbleText.text = editing.TraitName().Replace(" ", "\n");

        // コードを並べ直す（スタートとフィニッシュはそのまま）
        for (int i = codeList.childCount - 1; i >= 0; i--)
        {
            var ch = codeList.GetChild(i);
            if (ch.GetComponent<BlockView>() != null) Destroy(ch.gameObject);
        }
        var finish = codeList.Find("Finish");
        foreach (var b in code) MakeBlock(codeList, b, false);
        if (finish != null) finish.SetAsLastSibling();

        int used = Compiler.MemoryUsed(code);
        memoryText.text = used + " / " + editing.Capacity;
        memoryFill.fillAmount = Mathf.Clamp01((float)used / editing.Capacity);
        memoryFill.color = used > editing.Capacity ? UiKit.Hex("ff3b5c") : UiKit.Hex("3fb8ff");
        string sp = Compiler.SpeedLabel(used, editing.Capacity);
        speedText.text = sp;
        speedText.color = sp == "速い" ? UiKit.Hex("2a7de8") : sp == "ふつう" ? UiKit.Hex("2a9a4a") : UiKit.Hex("e0284a");

        bool ok = Compiler.Check(code, editing.Capacity, out string msg);
        if (ok)
        {
            var s = Compiler.Build(code);
            var t = battle.CurrentTarget();
            int dmg = Battle.Damage(editing, s, t);
            previewText.text = "見込み：1発 " + dmg + " ダメージ × " + s.Shots + "回" + (s.Target == TargetType.All ? "（敵全員）" : "（" + (t != null ? t.Name : "-") + "）");
        }
        else previewText.text = msg;
        if (!keepMessage) programMessage.text = "";
    }

    void AddLog(string s)
    {
        log.Add(s);
        if (log.Count > 3) log.RemoveAt(0);
        if (logText != null) logText.text = string.Join("\n", log);
    }

    // ================= 待機のアニメーション =================

    void AnimateUnits()
    {
        float t = Time.time;
        foreach (var kv in views)
        {
            var u = kv.Key;
            var v = kv.Value;
            if (v.Dead || v.Dying) continue;
            if (u.IsAlly)
            {
                // ぴょこぴょこ上下（ドット絵らしく 1 ドットずつ動かす）
                float step = Mathf.Round((Mathf.Sin(t * 3.0f + v.Phase) * 0.5f + 0.5f)) * AllyScale;
                v.Art.anchoredPosition = new Vector2(0, step) + v.Offset;
                v.Art.localScale = Vector3.one;
                v.Art.localRotation = Quaternion.Euler(0, 0, v.Tilt);
                if (v.PoseTimer > 0f) v.PoseTimer -= Time.deltaTime;
                else
                {
                    v.BlinkTimer -= Time.deltaTime;
                    if (v.BlinkTimer < -0.12f) { v.BlinkTimer = Random.Range(2f, 5f); v.ArtImage.sprite = v.Idle; }
                    else if (v.BlinkTimer < 0f) v.ArtImage.sprite = v.Blink;
                    else v.ArtImage.sprite = v.Idle;
                }
            }
            else
            {
                // ウイルスはふわふわ浮いてぷよぷよ
                float k = 1f + Mathf.Sin(t * 2.5f + v.Phase) * 0.05f;
                v.Art.anchoredPosition = new Vector2(0, 14f + Mathf.Sin(t * 1.7f + v.Phase) * 8f) + v.Offset;
                v.Art.localScale = new Vector3(k, 2f - k, 1f);
                v.Art.localRotation = Quaternion.Euler(0, 0, v.Tilt + Mathf.Sin(t * 1.3f + v.Phase) * 3f);
                v.GlitchTimer -= Time.deltaTime;
                if (v.GlitchTimer <= 0f && !intro)
                {
                    v.GlitchTimer = Random.Range(2.5f, 6f);
                    StartCoroutine(GlitchArt(v, 0.15f));
                }
            }
            if (v.Ring.gameObject.activeSelf) v.Ring.localRotation = Quaternion.Euler(0, 0, t * 40f);
        }
    }

    static Color ElementColor(Element e) =>
        e == Element.Fire ? new Color(1f, 0.5f, 0.15f) : e == Element.Ice ? new Color(0.45f, 0.9f, 1f) : new Color(0.95f, 0.85f, 1f);

    // ================= 行動のアニメーション =================

    IEnumerator PlayAction(ActionResult r)
    {
        busy = true;
        var actor = views[r.Actor];
        RectTransform icon;
        if (icons.TryGetValue(r.Actor, out icon)) StartCoroutine(Bump(icon));

        if (r.Actor.IsAlly && r.Actor.Spell != null)
        {
            var s = r.Actor.Spell;
            var color = ElementColor(s.Element);
            // 詠唱：魔法のポーズ、手の前に魔法陣
            actor.ArtImage.sprite = actor.Cast;
            actor.PoseTimer = 1.4f;
            var circle = MagicCircleAt(actor, color);
            yield return Lunge(actor, 1f, 18f);
            bool blast = s.HasEffect && s.Effect == Effect.Blast;
            foreach (var h in r.Hits)
            {
                if (h.Heal) continue;
                StartCoroutine(Projectile(actor, h, color, blast, s.HasEffect && s.Effect == Effect.Slow));
                yield return new WaitForSeconds(0.12f);
            }
            while (flying > 0) yield return null;
            if (circle != null) Destroy(circle.gameObject);
            foreach (var h in r.Hits)
                if (h.Heal) Impact(h, new Color(0.5f, 1f, 0.5f), false);
        }
        else if (r.Actor.IsAlly)
        {
            yield return Lunge(actor, 1f, 10f);
        }
        else
        {
            yield return Lunge(actor, -1f, 60f);
            StartCoroutine(Noise(0.3f, 0.45f));
            StartCoroutine(GlitchArt(actor, 0.25f));
            foreach (var h in r.Hits) Impact(h, new Color(0.8f, 0.3f, 1f), false); // ウイルスの侵食
        }

        yield return new WaitForSeconds(0.45f);
        busy = false;
        if (phase == Phase.Running && (battle.Won || battle.Lost))
        {
            phase = Phase.Finished;
            AddLog(battle.Won ? "ウイルスを全部駆除した！" : "システムダウン…");
            StartCoroutine(ShowResult(battle.Won));
        }
        RefreshBattle();
    }

    RectTransform MagicCircleAt(UnitView v, Color color)
    {
        var rt = Img(effects, UiKit.MagicCircle(), new Color(color.r, color.g, color.b, 0.9f)).rectTransform;
        rt.sizeDelta = new Vector2(130, 130);
        rt.position = v.Art.TransformPoint(new Vector3(v.Size.x * 0.5f, v.Size.y * 0.42f, 0));
        StartCoroutine(Spin(rt, 300f));
        return rt;
    }

    IEnumerator Spin(RectTransform rt, float speed)
    {
        while (rt != null)
        {
            rt.localRotation = Quaternion.Euler(0, 0, Time.time * speed);
            rt.localScale = new Vector3(0.45f, 1f, 1f);
            yield return null;
        }
    }

    // 相手の方へ踏み込んで戻る
    IEnumerator Lunge(UnitView v, float dir, float dist)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.12f) { v.Offset = new Vector2(dir * dist * t, 4f * t); v.Tilt = -dir * 5f * t; yield return null; }
        for (float t = 1f; t > 0f; t -= Time.deltaTime / 0.2f) { v.Offset = new Vector2(dir * dist * t, 4f * t); v.Tilt = -dir * 5f * t; yield return null; }
        v.Offset = Vector2.zero;
        v.Tilt = 0f;
    }

    IEnumerator Bump(RectTransform icon)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            if (icon == null) yield break;
            icon.localScale = Vector3.one * (1f + 0.4f * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        if (icon != null) icon.localScale = Vector3.one;
    }

    // 魔法の弾。山なりに飛んで、当たったら Impact
    IEnumerator Projectile(UnitView from, Hit h, Color color, bool big, bool slow)
    {
        flying++;
        var glow = Img(effects, UiKit.Glow(), new Color(color.r, color.g, color.b, 0.8f)).rectTransform;
        glow.sizeDelta = Vector2.one * (big ? 110f : 80f);
        var orb = Img(glow, UiKit.Circle(), color).rectTransform;
        orb.sizeDelta = Vector2.one * (big ? 34f : 24f);
        var core = Img(orb, UiKit.Circle(), Color.white).rectTransform;
        core.sizeDelta = Vector2.one * (big ? 16f : 11f);
        Vector3 a = from.Art.TransformPoint(new Vector3(from.Size.x * 0.5f, from.Size.y * 0.42f, 0));
        UnitView tv;
        views.TryGetValue(h.Target, out tv);
        Vector3 b = tv != null ? tv.Art.TransformPoint(new Vector3(0, tv.Size.y * 0.5f, 0)) : a;
        float trail = 0f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            var p = Vector3.Lerp(a, b, t);
            p.y += Mathf.Sin(t * Mathf.PI) * 90f * canvas.scaleFactor;
            glow.position = p;
            trail -= Time.deltaTime;
            if (trail <= 0f) { trail = 0.025f; Burst(p, color, 1, 12f, 40f); }
            yield return null;
        }
        Destroy(glow.gameObject);
        Impact(h, color, big);
        if (slow && tv != null) Burst(b, new Color(0.6f, 0.6f, 1f), 8, 12f, 140f);
        flying--;
    }

    // 当たった（回復した）ときの演出と、画面の HP の更新
    void Impact(Hit h, Color color, bool big)
    {
        if (!views.TryGetValue(h.Target, out var v)) return;
        v.ShownHp = Mathf.Clamp(v.ShownHp + (h.Heal ? h.Amount : -h.Amount), 0, h.Target.MaxHp);
        Vector3 center = v.Art.TransformPoint(new Vector3(0, v.Size.y * 0.5f, 0));
        ShowPopup(h, center);
        Burst(center, color, big ? 22 : 12, big ? 16f : 12f, big ? 480f : 300f);
        if (!h.Heal)
        {
            if (v.Hurt != null) { v.ArtImage.sprite = v.Hurt; v.PoseTimer = 0.5f; }
            StartCoroutine(ShakeUnit(v));
            StartCoroutine(Tint(v.ArtImage, new Color(1f, 0.5f, 0.5f)));
        }
        else StartCoroutine(Tint(v.ArtImage, new Color(0.6f, 1f, 0.6f)));
        if (v.ShownHp <= 0 && !v.Dying && !v.Dead) StartCoroutine(Die(v));
        RefreshBattle();
    }

    void ShowPopup(Hit h, Vector3 worldPos)
    {
        var t = MakeText(effects, (h.Heal ? "+" : "") + h.Amount, 56, h.Heal ? UiKit.Hex("7dff8a") : Color.white, TextAnchor.MiddleCenter, true);
        AddOutline(t, h.Heal ? UiKit.Hex("1a6a2a") : UiKit.Hex("c0103a"), 3f);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(240, 80);
        rt.position = worldPos;
        StartCoroutine(Popup(rt, t));
    }

    IEnumerator Popup(RectTransform rt, Text t)
    {
        var start = rt.anchoredPosition;
        for (float time = 0f; time < 0.9f; time += Time.deltaTime)
        {
            float jump = time < 0.2f ? Mathf.Sin(time / 0.2f * Mathf.PI) * 30f : 0f;
            rt.anchoredPosition = start + new Vector2(0, time * 70f + jump);
            var c = t.color; c.a = 1f - Mathf.Max(0f, time - 0.4f) / 0.5f; t.color = c;
            yield return null;
        }
        Destroy(rt.gameObject);
    }

    IEnumerator ShakeUnit(UnitView v)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            v.Offset = Random.insideUnitCircle * 12f * (1f - t);
            yield return null;
        }
        v.Offset = Vector2.zero;
    }

    IEnumerator Shake(RectTransform rt)
    {
        var start = rt.anchoredPosition;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            if (rt == null) yield break;
            rt.anchoredPosition = start + new Vector2(Random.Range(-8f, 8f) * (1f - t), 0);
            yield return null;
        }
        if (rt != null) rt.anchoredPosition = start;
    }

    IEnumerator Tint(Image img, Color color)
    {
        img.color = color;
        yield return new WaitForSeconds(0.15f);
        if (img != null) img.color = Color.white;
    }

    // 倒れる：ノイズのようにちらついて消える
    IEnumerator Die(UnitView v)
    {
        v.Dying = true;
        yield return new WaitForSeconds(0.25f);
        for (int i = 0; i < 10; i++)
        {
            if (v.Art == null) yield break;
            v.ArtImage.enabled = i % 2 == 0;
            v.Art.anchoredPosition = new Vector2(Random.Range(-14f, 14f), 0);
            v.Art.localScale = new Vector3(1f + Random.Range(-0.2f, 0.3f), 1f - i * 0.06f, 1f);
            yield return new WaitForSeconds(0.05f);
        }
        if (v.Art == null) yield break;
        v.ArtImage.enabled = true;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f)
        {
            v.Art.localScale = new Vector3(1f + t, Mathf.Max(0.02f, 0.4f * (1f - t)), 1f);
            yield return null;
        }
        v.ArtImage.enabled = false;
        v.Dying = false;
        v.Dead = true;
        RefreshBattle();
    }

    // 火花（小さな四角が飛び散って消える）
    void Burst(Vector3 pos, Color color, int count, float size, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            var sq = Box(effects, color);
            sq.sizeDelta = Vector2.one * size * Random.Range(0.6f, 1.2f);
            sq.position = pos;
            StartCoroutine(Spark(sq, Random.insideUnitCircle.normalized * speed * Random.Range(0.4f, 1f)));
        }
    }

    IEnumerator Spark(RectTransform sq, Vector2 vel)
    {
        var img = sq.GetComponent<Image>();
        var c0 = img.color;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
        {
            sq.anchoredPosition += vel * Time.deltaTime * (1f - t);
            img.color = new Color(c0.r, c0.g, c0.b, 1f - t);
            yield return null;
        }
        Destroy(sq.gameObject);
    }

    IEnumerator ShowResult(bool won)
    {
        yield return new WaitForSeconds(0.6f);
        resultPanel.SetActive(true);
        resultText.text = won ? "ウイルス駆除 完了！" : "システムダウン…";
        resultText.color = won ? Cyan : Magenta;
        StartCoroutine(Noise(0.5f, 0.6f));
    }

    // ================= ノイズ（ウイルスの侵入） =================

    // 登場演出：警告 → 砂嵐 → ウイルスが 1 体ずつ色ずれしながら現れる
    IEnumerator Intro()
    {
        intro = true;
        RefreshBattle();
        foreach (var e in battle.Enemies) { var v = views[e]; v.ArtImage.enabled = false; v.Dying = true; }

        yield return new WaitForSeconds(0.3f);
        var banner = Img(noiseLayer, null, new Color(0.75f, 0.05f, 0.2f, 0.92f)).rectTransform;
        Anchor(banner, 0, 0.5f, 1, 0.62f);
        var bt = MakeText(banner, "WARNING　ウイルスの侵入を検知しました", 52, Color.white, TextAnchor.MiddleCenter, true);
        Fill(bt.rectTransform);
        StartCoroutine(Noise(1.2f, 0.7f));
        for (int i = 0; i < 6; i++)
        {
            banner.gameObject.SetActive(i % 2 == 0);
            banner.anchoredPosition = new Vector2(Random.Range(-30f, 30f), 0);
            yield return new WaitForSeconds(0.18f);
        }
        Destroy(banner.gameObject);

        foreach (var e in battle.Enemies)
        {
            StartCoroutine(Noise(0.35f, 0.5f));
            yield return Materialize(views[e]);
        }
        intro = false;
        RefreshBattle();
    }

    IEnumerator Materialize(UnitView v)
    {
        v.ArtImage.enabled = true;
        StartCoroutine(GlitchArt(v, 0.45f));
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
        {
            if (v.Art == null) yield break;
            v.ArtImage.enabled = Random.value < 0.3f + t * 0.7f;
            v.Art.localScale = new Vector3(1f + (1f - t) * Random.Range(0f, 0.6f), Mathf.Max(0.05f, Mathf.Min(1.15f, t * 1.4f)), 1f);
            v.Art.anchoredPosition = new Vector2(Random.Range(-20f, 20f) * (1f - t), 14f);
            yield return null;
        }
        v.ArtImage.enabled = true;
        v.Art.localScale = Vector3.one;
        v.Dying = false;
    }

    // 絵の色ずれ（水色と赤紫の写しが横にずれて、ずれが戻る）
    IEnumerator GlitchArt(UnitView v, float dur)
    {
        if (v.Art == null || v.Dead) yield break;
        var ghosts = new List<Image>();
        foreach (var col in new[] { new Color(0f, 1f, 1f, 0.6f), new Color(1f, 0f, 0.8f, 0.6f) })
        {
            var img = Img(v.Art, v.ArtImage.sprite, col);
            Fill(img.rectTransform);
            ghosts.Add(img);
        }
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            float d = (1f - t) * 14f;
            for (int i = 0; i < ghosts.Count; i++)
            {
                if (ghosts[i] == null) yield break;
                ghosts[i].rectTransform.anchoredPosition = new Vector2((i == 0 ? -d : d) + Random.Range(-3f, 3f), Random.Range(-2f, 2f));
            }
            yield return null;
        }
        foreach (var g in ghosts) if (g != null) Destroy(g.gameObject);
    }

    // 画面全体の砂嵐と横すじ。strength は 0〜1
    IEnumerator Noise(float dur, float strength)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            float k = strength * (1f - t);
            noise.color = new Color(1, 1, 1, k * Random.Range(0.2f, 0.6f));
            noise.uvRect = new Rect(Random.value, Random.value, 3f, 3f);
            if (Random.value < k) StartCoroutine(Streak(k));
            yield return null;
        }
        noise.color = new Color(1, 1, 1, 0);
    }

    IEnumerator Streak(float k)
    {
        var colors = new[] { new Color(0f, 1f, 1f), new Color(1f, 0f, 0.8f), Color.white, new Color(0.3f, 1f, 0.4f) };
        var c = colors[Random.Range(0, colors.Length)];
        c.a = Random.Range(0.2f, 0.6f);
        float y = Random.value;
        float h = Random.Range(0.004f, 0.03f + 0.04f * k);
        float x0 = Random.Range(-0.2f, 0.4f);
        var bar = Img(noiseLayer, null, c).rectTransform;
        Anchor(bar, x0, y, x0 + Random.Range(0.3f, 1f), Mathf.Min(1f, y + h));
        yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
        Destroy(bar.gameObject);
    }

    // ================= 画面の組み立て：共通 =================

    void BuildCanvas()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            // Input System のパッケージが入っていればそれを使い、無ければ標準の入力を使う
            // （パッケージが無くてもコンパイルできるよう、名前で探す）
            var inputModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModule != null) es.AddComponent(inputModule);
            else es.AddComponent<StandaloneInputModule>();
        }
        var canvasGo = new GameObject("UI");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        root = (RectTransform)canvasGo.transform;
    }

    RectTransform Layer(string name)
    {
        var rt = NewRect(root, name);
        Fill(rt);
        return rt;
    }

    void BuildNoise()
    {
        noiseLayer = Layer("Noise");
        var scan = NewRect(noiseLayer, "Scanlines").gameObject.AddComponent<RawImage>();
        Fill(scan.rectTransform);
        scan.texture = UiKit.Scanlines();
        scan.uvRect = new Rect(0, 0, 1, 1080f / 4f);
        scan.raycastTarget = false;
        noise = NewRect(noiseLayer, "Static").gameObject.AddComponent<RawImage>();
        Fill(noise.rectTransform);
        noise.texture = UiKit.Static();
        noise.color = new Color(1, 1, 1, 0);
        noise.raycastTarget = false;
    }

    // ================= タイトル画面 =================

    void BuildTitle()
    {
        titleLayer = Layer("Title");
        var bg = NewRect(titleLayer, "Bg").gameObject.AddComponent<RawImage>();
        Fill(bg.rectTransform);
        bg.texture = UiKit.DiagonalGradient(UiKit.Hex("ffb6df"), UiKit.Hex("9fd6ff"));
        bg.raycastTarget = false;

        // 魔法陣（ゆっくり回る）
        var circles = new[] { new[] { 0.18f, 0.2f, 760f, 6f }, new[] { 0.82f, 0.82f, 520f, -9f }, new[] { 0.62f, 0.12f, 360f, 14f } };
        foreach (var cc in circles)
        {
            float x = cc[0], y = cc[1], size = cc[2], speed = cc[3];
            var mc = Img(titleLayer, UiKit.MagicCircle(), new Color(1, 1, 1, 0.35f)).rectTransform;
            mc.anchorMin = mc.anchorMax = new Vector2(x, y);
            mc.sizeDelta = new Vector2(size, size);
            ambient.StartCoroutine(SpinForever(mc, speed));
        }
        // きらきら
        for (int i = 0; i < 26; i++)
        {
            var sp = Img(titleLayer, UiKit.Glow(), new Color(1, 1, 1, 0.8f)).rectTransform;
            sp.anchorMin = sp.anchorMax = new Vector2(Random.value, Random.value);
            sp.sizeDelta = Vector2.one * Random.Range(10f, 34f);
            ambient.StartCoroutine(Float(sp));
        }

        // 3人（ピンクが前、青と黄色が後ろ）
        TitleGirl("blue_idle", 0.64f, 0.1f, 7.5f, new Color(0.93f, 0.93f, 1f));
        TitleGirl("yellow_idle", 0.9f, 0.1f, 7.5f, new Color(0.93f, 0.93f, 1f));
        TitleGirl("red_idle", 0.77f, 0.03f, 9.5f, Color.white);

        // ロゴ
        var logo = Img(titleLayer, UiKit.RoundRect(40), new Color(1, 1, 1, 0.88f));
        logo.type = Image.Type.Sliced;
        Anchor(logo.rectTransform, 0.04f, 0.47f, 0.53f, 0.92f);
        var hat = Img(logo.rectTransform, UiKit.Art("hat"), Color.white).rectTransform;
        hat.anchorMin = hat.anchorMax = new Vector2(0.1f, 0.8f);
        hat.sizeDelta = UiKit.ArtSize("hat") * 4.4f;
        hat.localRotation = Quaternion.Euler(0, 0, 14f);
        var magical = MakeText(logo.rectTransform, "マジカル", 150, Pink, TextAnchor.MiddleLeft, true);
        Anchor(magical.rectTransform, 0.06f, 0.52f, 0.96f, 0.98f);
        AddOutline(magical, Color.white, 6f);
        var shadow = magical.gameObject.AddComponent<Shadow>();
        shadow.effectColor = Purple;
        shadow.effectDistance = new Vector2(8, -8);
        // 「コンパイル」は緑の字に色ずれ
        var compile = MakeText(logo.rectTransform, "コンパイル", 136, UiKit.Hex("3fe04a"), TextAnchor.MiddleLeft, true);
        Anchor(compile.rectTransform, 0.06f, 0.04f, 0.98f, 0.5f);
        AddOutline(compile, UiKit.Hex("123a18"), 5f);
        for (int k = 0; k < 2; k++)
        {
            var c = k == 0 ? new Color(1f, 0f, 0.8f, 0.7f) : new Color(0f, 1f, 1f, 0.7f);
            float dx = k == 0 ? -6f : 6f;
            var g = MakeText(compile.rectTransform, "コンパイル", 136, c, TextAnchor.MiddleLeft, true);
            Fill(g.rectTransform);
            g.rectTransform.anchoredPosition = new Vector2(dx, 0);
            g.transform.SetAsFirstSibling();
            ambient.StartCoroutine(TitleGlitch(g.rectTransform, dx));
        }
        Chip(logo.rectTransform, new Vector2(0.86f, 0.42f));

        // スタート
        var start = CommandButton(titleLayer, "スタート", 54, OnTitleStart, true);
        Anchor((RectTransform)start.transform, 0.12f, 0.24f, 0.44f, 0.34f);
        ambient.StartCoroutine(Pulse((RectTransform)start.transform));
        var hint = MakeText(titleLayer, "クリックしてはじめる", 28, Ink, TextAnchor.MiddleCenter, false);
        Anchor(hint.rectTransform, 0.12f, 0.17f, 0.44f, 0.23f);
        ambient.StartCoroutine(BlinkText(hint));
        var about = MakeText(titleLayer, "プログラムを組んで魔法を作り、パソコンに入りこんだウイルスを撃退しよう", 26, Ink, TextAnchor.MiddleLeft, false);
        Anchor(about.rectTransform, 0.04f, 0.38f, 0.6f, 0.45f);
    }

    void TitleGirl(string art, float x, float y, float scale, Color tint)
    {
        var shadow = Img(titleLayer, UiKit.Glow(), new Color(0.3f, 0.1f, 0.4f, 0.35f)).rectTransform;
        shadow.anchorMin = shadow.anchorMax = new Vector2(x, y);
        shadow.sizeDelta = new Vector2(30, 7) * scale;
        var img = Img(titleLayer, UiKit.Art(art), tint);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(x, y);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = UiKit.ArtSize(art) * scale;
        ambient.StartCoroutine(TitleBob(rt, img, art, scale));
    }

    // タイトルの女の子：ぴょこぴょこ上下して、ときどきまばたき
    IEnumerator TitleBob(RectTransform rt, Image img, string art, float scale)
    {
        var start = rt.anchoredPosition;
        float ph = Random.value * 6f, blink = Random.Range(1f, 4f);
        var idle = UiKit.Art(art);
        var closed = UiKit.Art(art.Replace("_idle", "_blink"));
        while (true)
        {
            rt.anchoredPosition = start + new Vector2(0, Mathf.Round(Mathf.Sin(Time.time * 2.4f + ph) * 0.5f + 0.5f) * scale);
            blink -= Time.deltaTime;
            img.sprite = blink < 0f ? closed : idle;
            if (blink < -0.12f) blink = Random.Range(2f, 5f);
            yield return null;
        }
    }

    IEnumerator TitleGlitch(RectTransform rt, float dx)
    {
        while (true)
        {
            rt.anchoredPosition = new Vector2(dx, 0);
            yield return new WaitForSeconds(Random.Range(1.5f, 3.5f));
            for (int i = 0; i < 6; i++)
            {
                rt.anchoredPosition = new Vector2(dx * Random.Range(1f, 4f), Random.Range(-6f, 6f));
                yield return new WaitForSeconds(0.04f);
            }
        }
    }

    IEnumerator SpinForever(RectTransform rt, float speed)
    {
        while (true) { rt.localRotation = Quaternion.Euler(0, 0, Time.time * speed); yield return null; }
    }

    IEnumerator Float(RectTransform rt)
    {
        var img = rt.GetComponent<Image>();
        float ph = Random.value * 10f, sp = Random.Range(0.02f, 0.06f);
        while (true)
        {
            var a = rt.anchorMin;
            a.y += sp * Time.deltaTime;
            if (a.y > 1.05f) { a.y = -0.05f; a.x = Random.value; }
            rt.anchorMin = rt.anchorMax = a;
            img.color = new Color(1, 1, 1, 0.4f + 0.4f * Mathf.Sin(Time.time * 3f + ph));
            yield return null;
        }
    }

    IEnumerator Pulse(RectTransform rt)
    {
        while (true) { rt.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(Time.time * 4f)); yield return null; }
    }

    IEnumerator BlinkText(Text t)
    {
        while (true) { t.color = new Color(t.color.r, t.color.g, t.color.b, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f)); yield return null; }
    }

    // ロゴの横の IC チップ
    void Chip(RectTransform parent, Vector2 at)
    {
        var chipImg = Img(parent, UiKit.RoundRect(10), UiKit.Hex("2b2b33"));
        chipImg.type = Image.Type.Sliced;
        var chip = chipImg.rectTransform;
        chip.anchorMin = chip.anchorMax = at;
        chip.sizeDelta = new Vector2(150, 150);
        var inner = Img(chip, UiKit.RoundRect(6), UiKit.Hex("4a4a56"));
        inner.type = Image.Type.Sliced;
        Anchor(inner.rectTransform, 0.18f, 0.18f, 0.82f, 0.82f);
        var colors = new[] { Pink, UiKit.Hex("3cc0e8") };
        var dirs = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        for (int i = 0; i < 4; i++)
        {
            float f = 0.2f + i * 0.2f;
            for (int d = 0; d < 4; d++)
            {
                var dir = dirs[d];
                var pin = Box(chip, UiKit.Hex("2b2b33"));
                bool vert = dir.y != 0;
                pin.anchorMin = pin.anchorMax = vert ? new Vector2(f, dir.y > 0 ? 1f : 0f) : new Vector2(dir.x > 0 ? 1f : 0f, f);
                pin.pivot = new Vector2(vert ? 0.5f : (dir.x > 0 ? 0f : 1f), vert ? (dir.y > 0 ? 0f : 1f) : 0.5f);
                float len = (i % 2 == 0) ? 40f : 26f;
                pin.sizeDelta = vert ? new Vector2(5, len) : new Vector2(len, 5);
                var dot = Img(pin, UiKit.Circle(), colors[(i + d) % 2]).rectTransform;
                dot.anchorMin = dot.anchorMax = vert ? new Vector2(0.5f, dir.y > 0 ? 1f : 0f) : new Vector2(dir.x > 0 ? 1f : 0f, 0.5f);
                dot.sizeDelta = new Vector2(16, 16);
            }
        }
    }

    // ================= 戦闘画面 =================

    void BuildBattle()
    {
        battleLayer = Layer("Battle");
        // 背景：パソコンの中。上は暗い紫、下は奥へ続く光る床
        var sky = NewRect(battleLayer, "Sky").gameObject.AddComponent<RawImage>();
        Fill(sky.rectTransform);
        sky.texture = UiKit.VerticalGradient(UiKit.Hex("3a1660"), UiKit.Hex("0c0a22"));
        sky.raycastTarget = false;
        var circuit = NewRect(battleLayer, "Circuit").gameObject.AddComponent<RawImage>();
        Anchor(circuit.rectTransform, 0, 0.42f, 1, 1);
        circuit.texture = CircuitTexture();
        circuit.uvRect = new Rect(0, 0, 1920f / 96f, 626f / 96f);
        circuit.color = new Color(1, 1, 1, 0.5f);
        circuit.raycastTarget = false;
        var floor = NewRect(battleLayer, "Floor").gameObject.AddComponent<RawImage>();
        Anchor(floor.rectTransform, 0, 0, 1, 0.44f);
        floor.texture = UiKit.FloorGrid(UiKit.Hex("1c0b3a"), UiKit.Hex("ff4fd8"));
        floor.raycastTarget = false;
        var horizon = Img(battleLayer, UiKit.Glow(), new Color(0.3f, 0.95f, 1f, 0.55f)).rectTransform;
        Anchor(horizon, -0.2f, 0.38f, 1.2f, 0.5f);
        var flat = NewRect(battleLayer, "FloorCircle");
        flat.anchorMin = flat.anchorMax = new Vector2(0.5f, 0.24f);
        flat.localScale = new Vector3(1f, 0.28f, 1f);
        var bigCircle = Img(flat, UiKit.MagicCircle(), new Color(1f, 0.4f, 0.9f, 0.18f)).rectTransform;
        bigCircle.sizeDelta = new Vector2(1300, 1300);
        ambient.StartCoroutine(SpinForever(bigCircle, 4f));

        stage = NewRect(battleLayer, "Stage");
        Fill(stage);

        // ターン表示（左上の黒い矢印）
        var turn = Img(battleLayer, UiKit.Arrow(), new Color(0.02f, 0.02f, 0.04f, 0.95f));
        turn.type = Image.Type.Sliced;
        Anchor(turn.rectTransform, 0, 0.89f, 0.2f, 0.97f);
        turnText = MakeText(turn.rectTransform, "ターン1", 54, Color.white, TextAnchor.MiddleLeft, true);
        turnText.fontStyle = FontStyle.BoldAndItalic;
        Anchor(turnText.rectTransform, 0, 0, 1, 1, 34, 0, 0, 0);

        // ログ（右上）
        var logBox = Img(battleLayer, UiKit.RoundRect(16), new Color(0.05f, 0.03f, 0.12f, 0.7f));
        logBox.type = Image.Type.Sliced;
        Anchor(logBox.rectTransform, 0.24f, 0.88f, 0.985f, 0.98f);
        logText = MakeText(logBox.rectTransform, "", 22, Color.white, TextAnchor.MiddleLeft, false);
        Anchor(logText.rectTransform, 0, 0, 1, 1, 20, 4, -20, -4);

        // 味方の HP（左下）
        hudPanel = NewRect(battleLayer, "Hud");
        Anchor(hudPanel, 0.01f, 0.015f, 0.4f, 0.21f);

        // コマンド
        commandMenu = NewRect(battleLayer, "Commands");
        Anchor(commandMenu, 0.37f, 0.42f, 0.56f, 0.84f);
        var titleBox = Img(commandMenu, UiKit.RoundRect(12), new Color(0.05f, 0.03f, 0.12f, 0.8f));
        titleBox.type = Image.Type.Sliced;
        Anchor(titleBox.rectTransform, 0, 0.8f, 1, 1);
        commandTitle = MakeText(titleBox.rectTransform, "", 30, Color.white, TextAnchor.MiddleLeft, true);
        commandTitle.supportRichText = true;
        Anchor(commandTitle.rectTransform, 0, 0, 1, 1, 16, 4, -10, -4);
        var names = new[] { "プログラム", "アイテム", "チャージ", "リンクプログラム" };
        for (int i = 0; i < names.Length; i++)
        {
            string n = names[i];
            UnityEngine.Events.UnityAction act = i == 0 ? (UnityEngine.Events.UnityAction)OnProgramCommand : () => OnNotReady(n);
            var b = CommandButton(commandMenu, n, 34, act, false);
            Anchor((RectTransform)b.transform, i * 0.03f, 0.64f - i * 0.135f, 1f, 0.76f - i * 0.135f);
            if (i > 0) b.GetComponentInChildren<Text>().color = new Color(0.55f, 0.6f, 0.65f);
            commandButtons.Add(b);
        }
        var go = CommandButton(commandMenu, "バトル開始", 38, OnStartButton, true);
        Anchor((RectTransform)go.transform, 0.06f, 0f, 1.06f, 0.11f);
        commandButtons.Add(go);

        pointerLine = Img(battleLayer, null, Pink).rectTransform;
        pointerLine.anchorMin = pointerLine.anchorMax = new Vector2(0.5f, 0.5f);

        // タイムライン（右下）
        var tl = NewRect(battleLayer, "Timeline");
        Anchor(tl, 0.42f, 0.03f, 0.985f, 0.22f);
        var tlBg = Img(tl, UiKit.RoundRect(16), new Color(0.05f, 0.03f, 0.12f, 0.55f));
        tlBg.type = Image.Type.Sliced;
        Fill(tlBg.rectTransform);
        var tlLabel = Img(tl, UiKit.Slant(), new Color(0.02f, 0.02f, 0.04f, 0.95f));
        tlLabel.type = Image.Type.Sliced;
        Anchor(tlLabel.rectTransform, 0, 1f, 0.32f, 1f, 0, -6, 0, 40);
        var tlText = MakeText(tlLabel.rectTransform, "タイムライン", 28, Color.white, TextAnchor.MiddleCenter, true);
        Fill(tlText.rectTransform);
        timelineBar = Img(tl, null, UiKit.Hex("5ee05a")).rectTransform;
        Anchor(timelineBar, 0.05f, 0.5f, 0.92f, 0.5f, 0, -8, 0, 8);
        var head = Img(timelineBar, UiKit.Arrow(), UiKit.Hex("5ee05a")).rectTransform;
        head.anchorMin = head.anchorMax = new Vector2(1, 0.5f);
        head.pivot = new Vector2(0, 0.5f);
        head.sizeDelta = new Vector2(46, 44);

        // 勝ち・負け
        var rp = Img(battleLayer, null, new Color(0, 0, 0, 0.6f));
        rp.raycastTarget = true;
        Fill(rp.rectTransform);
        resultPanel = rp.gameObject;
        var band = Img(rp.rectTransform, null, new Color(0.05f, 0.02f, 0.12f, 0.9f)).rectTransform;
        Anchor(band, 0, 0.42f, 1, 0.66f);
        resultText = MakeText(band, "", 96, Cyan, TextAnchor.MiddleCenter, true);
        AddOutline(resultText, Color.black, 4f);
        var again = CommandButton(rp.rectTransform, "もう一度", 36, NewBattle, true);
        Anchor((RectTransform)again.transform, 0.3f, 0.26f, 0.48f, 0.34f);
        var back = CommandButton(rp.rectTransform, "タイトルへ", 36, ShowTitle, false);
        Anchor((RectTransform)back.transform, 0.52f, 0.26f, 0.7f, 0.34f);
        resultPanel.SetActive(false);
    }

    void BuildUnits()
    {
        ClearChildren(stage);
        ClearChildren(hudPanel);
        foreach (var i in icons.Values) Destroy(i.gameObject);
        views.Clear();
        allyHud.Clear();
        icons.Clear();

        // 足もとの位置（画面の割合）。真ん中のキャラが少し手前
        var allyPos = new[] { new Vector2(0.09f, 0.29f), new Vector2(0.2f, 0.24f), new Vector2(0.31f, 0.29f) };
        var enemyPos = new[] { new Vector2(0.64f, 0.3f), new Vector2(0.76f, 0.25f), new Vector2(0.89f, 0.31f) };
        var enemyScale = new[] { 6f, 6f, 7.5f };
        // 奥のキャラから順に置く（手前のキャラが上に重なるように）
        var order = new List<(Unit u, Vector2 pos, float scale)>();
        for (int i = 0; i < 3; i++) order.Add((battle.Allies[i], allyPos[i], AllyScale));
        for (int i = 0; i < 3; i++) order.Add((battle.Enemies[i], enemyPos[i], enemyScale[i]));
        order.Sort((a, b) => b.pos.y.CompareTo(a.pos.y));
        foreach (var o in order) views[o.u] = MakeUnit(o.u, o.pos, o.scale);

        // 味方の HP（顔と HP の数字）
        for (int i = 0; i < battle.Allies.Count; i++)
        {
            var u = battle.Allies[i];
            var box = NewRect(hudPanel, "Hud");
            Anchor(box, i / 3f, 0, (i + 1) / 3f - 0.01f, 1);
            box.gameObject.AddComponent<CanvasGroup>();
            var face = Img(box, UiKit.Art(u.Art + "_idle", FaceCrop), Color.white).rectTransform;
            face.anchorMin = face.anchorMax = new Vector2(0.5f, 1f);
            face.pivot = new Vector2(0.5f, 1f);
            face.sizeDelta = new Vector2(30, 27) * 4f;
            var plate = Img(box, UiKit.Slant(), new Color(0.12f, 0.12f, 0.16f, 0.95f));
            plate.type = Image.Type.Sliced;
            plate.raycastTarget = true;
            Anchor(plate.rectTransform, 0, 0, 1, 0.38f);
            var hp = MakeText(plate.rectTransform, "HP300", 40, Cyan, TextAnchor.MiddleCenter, true);
            hp.fontStyle = FontStyle.BoldAndItalic;
            AddOutline(hp, UiKit.Hex("0a3a48"), 2f);
            Anchor(hp.rectTransform, 0, 0.18f, 1, 1);
            var back = Img(plate.rectTransform, null, new Color(0, 0, 0, 0.6f)).rectTransform;
            Anchor(back, 0.12f, 0.1f, 0.88f, 0.2f);
            var fill = Img(back, UiKit.RoundRect(2), Cyan);
            Fill(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            var btn = box.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            btn.onClick.AddListener(() => OnAllyClicked(u));
            allyHud[u] = (hp, fill, box);
        }

        // タイムラインのアイコン（味方は顔でバーの上、敵はバーの下）
        foreach (var u in battle.Allies) icons[u] = MakeIcon(UiKit.Art(u.Art + "_idle", FaceCrop), 44f, Cyan);
        foreach (var e in battle.Enemies) icons[e] = MakeIcon(UiKit.Art(e.Art), -44f, Magenta);
    }

    UnitView MakeUnit(Unit u, Vector2 pos, float scale)
    {
        string idle = u.IsAlly ? u.Art + "_idle" : u.Art;
        var v = new UnitView { U = u, Phase = Random.Range(0f, 6.28f), BlinkTimer = Random.Range(1f, 4f), GlitchTimer = Random.Range(2f, 5f), ShownHp = u.Hp };
        v.Root = NewRect(stage, u.Name);
        v.Root.anchorMin = v.Root.anchorMax = pos;
        v.Root.sizeDelta = Vector2.zero;

        var shadow = Img(v.Root, UiKit.Glow(), new Color(0, 0, 0, 0.55f)).rectTransform;
        shadow.sizeDelta = new Vector2(30, 7) * scale;
        var ringFlat = NewRect(v.Root, "RingFlat");   // 魔法陣を床に寝かせる（縦につぶす）
        ringFlat.localScale = new Vector3(1f, 0.3f, 1f);
        v.Ring = Img(ringFlat, UiKit.MagicCircle(), u.IsAlly ? new Color(1f, 0.35f, 0.8f, 0.9f) : new Color(1f, 0.25f, 0.3f, 0.9f)).rectTransform;
        v.Ring.sizeDelta = new Vector2(36, 36) * scale;

        v.ArtImage = Img(v.Root, UiKit.Art(idle), Color.white);
        v.Art = v.ArtImage.rectTransform;
        v.Art.pivot = new Vector2(0.5f, 0f);
        v.Size = UiKit.ArtSize(idle) * scale;
        v.Art.sizeDelta = v.Size;
        v.ArtImage.raycastTarget = true;
        var btn = v.Art.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        if (u.IsAlly) btn.onClick.AddListener(() => OnAllyClicked(u));
        else btn.onClick.AddListener(() => OnEnemyClicked(u));

        v.Idle = UiKit.Art(idle);
        if (u.IsAlly)
        {
            v.Blink = UiKit.Art(u.Art + "_blink");
            v.Cast = UiKit.Art(u.Art + "_cast");
            v.Hurt = UiKit.Art(u.Art + "_hurt");
        }
        else
        {
            // 敵の HP（頭の上の黒い帯にピンクの字）
            var box = Img(v.Root, UiKit.Slant(), new Color(0.06f, 0.06f, 0.09f, 0.92f));
            box.type = Image.Type.Sliced;
            v.HpBox = box.rectTransform;
            v.HpBox.anchoredPosition = new Vector2(0, v.Size.y + 70f);
            v.HpBox.sizeDelta = new Vector2(170, 54);
            v.HpText = MakeText(v.HpBox, "", 34, Magenta, TextAnchor.MiddleCenter, true);
            v.HpText.fontStyle = FontStyle.BoldAndItalic;
            AddOutline(v.HpText, UiKit.Hex("3a0a30"), 2f);
            Anchor(v.HpText.rectTransform, 0, 0.2f, 1, 1);
            var back = Img(v.HpBox, null, new Color(0, 0, 0, 0.6f)).rectTransform;
            Anchor(back, 0.12f, 0.1f, 0.88f, 0.22f);
            v.HpFill = Img(back, UiKit.RoundRect(2), Magenta);
            Fill(v.HpFill.rectTransform);
            v.HpFill.type = Image.Type.Filled;
            v.HpFill.fillMethod = Image.FillMethod.Horizontal;
            var weak = MakeText(v.HpBox, u.HasWeakness ? "弱点 " + Blocks.Label(BlockKind.Element, (int)u.Weakness) : "弱点なし", 20, Color.white, TextAnchor.MiddleCenter, true);
            Anchor(weak.rectTransform, 0, 1, 1, 1, 0, 0, 0, 26);
            AddOutline(weak, Color.black, 2f);
        }
        return v;
    }

    RectTransform MakeIcon(Sprite sprite, float y, Color frame)
    {
        var icon = Img(timelineBar, UiKit.Circle(), frame).rectTransform;
        icon.anchorMin = icon.anchorMax = new Vector2(0, 0.5f);
        icon.sizeDelta = new Vector2(70, 70);
        icon.anchoredPosition = new Vector2(0, y);
        var inner = Img(icon, UiKit.Circle(), new Color(0.12f, 0.08f, 0.22f)).rectTransform;
        Anchor(inner, 0, 0, 1, 1, 4, 4, -4, -4);
        var img = Img(inner, sprite, Color.white);
        img.preserveAspect = true;
        Anchor(img.rectTransform, 0, 0, 1, 1, 4, 4, -4, -4);
        return icon;
    }

    // 背景の電子回路の方眼（96×96 をくり返す）
    static Texture2D CircuitTexture()
    {
        var tex = new Texture2D(96, 96, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;
        var clear = new Color(0, 0, 0, 0);
        var line = new Color(0.35f, 0.9f, 1f, 0.35f);
        var dot = new Color(1f, 0.4f, 0.9f, 0.8f);
        for (int y = 0; y < 96; y++)
            for (int x = 0; x < 96; x++)
            {
                var c = clear;
                if ((y == 20 && x < 60) || (x == 60 && y >= 20 && y < 70) || (y == 70 && x >= 60) || (x == 20 && y > 20 && y < 50) || (y == 50 && x < 20))
                    c = line;
                if ((Mathf.Abs(x - 20) <= 2 && Mathf.Abs(y - 50) <= 2) || (Mathf.Abs(x - 60) <= 2 && Mathf.Abs(y - 70) <= 2) || (Mathf.Abs(x - 88) <= 1 && Mathf.Abs(y - 8) <= 1))
                    c = dot;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    // ================= プログラム画面 =================

    void BuildProgramScreen()
    {
        var screen = NewRect(root, "Program");
        Fill(screen);
        programScreen = screen.gameObject;
        var bg = screen.gameObject.AddComponent<RawImage>();
        bg.texture = UiKit.DiagonalGradient(UiKit.Hex("ffc3e6"), UiKit.Hex("b4e0ff"));
        var mc = Img(screen, UiKit.MagicCircle(), new Color(1, 1, 1, 0.35f)).rectTransform;
        mc.anchorMin = mc.anchorMax = new Vector2(0.88f, 0.2f);
        mc.sizeDelta = new Vector2(700, 700);
        ambient.StartCoroutine(SpinForever(mc, 5f));

        // 見出し（青い斜めの帯）
        var header = Img(screen, UiKit.Slant(), UiKit.Hex("1e5cf0"));
        header.type = Image.Type.Sliced;
        Anchor(header.rectTransform, -0.02f, 0.9f, 0.26f, 0.98f);
        var ht = MakeText(header.rectTransform, "プログラム", 52, Color.white, TextAnchor.MiddleCenter, true);
        ht.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(ht, UiKit.Hex("0a1c60"), 3f);

        // ロボットを切りかえるタブ（顔と名前）
        robotTabs.Clear();
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var tab = Img(screen, UiKit.RoundRect(18), Color.white);
            tab.type = Image.Type.Sliced;
            tab.raycastTarget = true;
            Anchor(tab.rectTransform, 0.3f + i * 0.13f, 0.9f, 0.42f + i * 0.13f, 0.98f);
            var b = tab.gameObject.AddComponent<Button>();
            b.targetGraphic = tab;
            b.onClick.AddListener(() =>
            {
                var u = battle.Allies[idx];
                if (!u.Alive) return;
                editing = u;
                RefreshProgram();
            });
            var face = Img(tab.rectTransform, null, Color.white);
            face.preserveAspect = true;
            Anchor(face.rectTransform, 0, 0, 0.36f, 1, 6, 4, 0, -4);
            var t = MakeText(tab.rectTransform, "", 28, Ink, TextAnchor.MiddleLeft, true);
            t.supportRichText = true;
            Anchor(t.rectTransform, 0.38f, 0, 1, 1);
            robotTabs.Add((null, tab, t, face));
        }

        // ウィンドウ（「コード」のタブ付き）
        var winTab = Img(screen, UiKit.TabShape(), UiKit.Hex("e9d6f7"));
        winTab.type = Image.Type.Sliced;
        Anchor(winTab.rectTransform, 0.02f, 0.815f, 0.13f, 0.87f);
        MakeText(winTab.rectTransform, "コード", 26, Ink, TextAnchor.MiddleCenter, false);
        var winBorder = Img(screen, UiKit.RoundRect(28), Purple);
        winBorder.type = Image.Type.Sliced;
        Anchor(winBorder.rectTransform, 0.02f, 0.03f, 0.66f, 0.825f);
        var win = Img(winBorder.rectTransform, UiKit.RoundRect(24), UiKit.Hex("f7eef8"));
        win.type = Image.Type.Sliced;
        Anchor(win.rectTransform, 0, 0, 1, 1, 5, 5, -5, -5);

        // 手持ちブロック
        var palette = Img(win.rectTransform, UiKit.RoundRect(18), UiKit.Hex("eab8dd"));
        palette.type = Image.Type.Sliced;
        Anchor(palette.rectTransform, 0.02f, 0.03f, 0.37f, 0.97f);
        var pTitle = MakeText(palette.rectTransform, "手持ちブロック", 30, Ink, TextAnchor.UpperLeft, false);
        Anchor(pTitle.rectTransform, 0, 1, 1, 1, 18, -56, 0, -12);
        paletteList = NewRect(palette.rectTransform, "List");
        Anchor(paletteList, 0, 0, 1, 1, 16, 16, -16, -70);
        AddVertical(paletteList, 12);
        foreach (var k in Blocks.Order) MakeBlock(paletteList, new Block(k), true);
        var hint = MakeText(paletteList, "ドラッグして右のコードへ。\n▼ をクリックで中身が変わる", 20, Ink, TextAnchor.UpperLeft, false);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;

        // コード
        var codeBg = Img(win.rectTransform, UiKit.RoundRect(18), UiKit.Hex("efc4e6"));
        codeBg.type = Image.Type.Sliced;
        Anchor(codeBg.rectTransform, 0.39f, 0.03f, 0.98f, 0.97f);
        codeArea = codeBg.rectTransform;
        var cTitle = MakeText(codeArea, "コード", 30, Ink, TextAnchor.UpperLeft, false);
        Anchor(cTitle.rectTransform, 0, 1, 1, 1, 18, -56, 0, -12);
        codeList = NewRect(codeArea, "Code");
        Anchor(codeList, 0, 0, 1, 1, 34, 16, -34, -64);
        AddVertical(codeList, -UiKit.BlockTabHeight + 2f);   // 下の出っぱりを次のブロックのへこみに重ねる
        MakeCapBlock(codeList, "スタート", "Start", true);
        MakeCapBlock(codeList, "フィニッシュ", "Finish", false);

        // 右：ロボット・メモリ・処理速度・送信
        var side = Img(screen, UiKit.RoundRect(28), new Color(1, 1, 1, 0.9f));
        side.type = Image.Type.Sliced;
        var sideRt = side.rectTransform;
        Anchor(sideRt, 0.68f, 0.03f, 0.985f, 0.87f);
        robotPortrait = Img(sideRt, null, Color.white);
        robotPortrait.preserveAspect = true;
        Anchor(robotPortrait.rectTransform, 0.02f, 0.52f, 0.5f, 0.99f);
        robotName = MakeText(sideRt, "", 34, Ink, TextAnchor.MiddleCenter, true);
        Anchor(robotName.rectTransform, 0.02f, 0.46f, 0.5f, 0.52f);
        var bubble = Img(sideRt, UiKit.RoundRect(40), new Color(0.12f, 0.1f, 0.16f, 0.85f));
        bubble.type = Image.Type.Sliced;
        Anchor(bubble.rectTransform, 0.52f, 0.66f, 0.97f, 0.95f);
        bubbleText = MakeText(bubble.rectTransform, "", 36, Color.white, TextAnchor.MiddleCenter, true);
        foreach (var dd in new[] { new[] { 0.5f, 0.62f, 18f }, new[] { 0.46f, 0.58f, 11f } })
        {
            float x = dd[0], y = dd[1], r = dd[2];
            var dot = Img(sideRt, UiKit.Circle(), new Color(0.12f, 0.1f, 0.16f, 0.85f)).rectTransform;
            dot.anchorMin = dot.anchorMax = new Vector2(x, y);
            dot.sizeDelta = new Vector2(r, r) * 2f;
        }

        var memLabel = MakeText(sideRt, "メモリ", 28, Ink, TextAnchor.MiddleLeft, true);
        Anchor(memLabel.rectTransform, 0.52f, 0.55f, 0.97f, 0.62f);
        memoryText = MakeText(sideRt, "", 40, Ink, TextAnchor.MiddleRight, true);
        Anchor(memoryText.rectTransform, 0.52f, 0.49f, 0.95f, 0.56f);
        var memBack = Img(sideRt, UiKit.RoundRect(10), UiKit.Hex("d8d0e8"));
        memBack.type = Image.Type.Sliced;
        Anchor(memBack.rectTransform, 0.53f, 0.455f, 0.95f, 0.485f);
        memoryFill = Img(memBack.rectTransform, UiKit.RoundRect(10), Cyan);
        Fill(memoryFill.rectTransform);
        memoryFill.type = Image.Type.Filled;
        memoryFill.fillMethod = Image.FillMethod.Horizontal;

        var spLabel = MakeText(sideRt, "処理速度", 28, Ink, TextAnchor.MiddleLeft, true);
        Anchor(spLabel.rectTransform, 0.05f, 0.36f, 0.5f, 0.43f);
        speedText = MakeText(sideRt, "", 58, Ink, TextAnchor.MiddleCenter, true);
        Anchor(speedText.rectTransform, 0.45f, 0.34f, 0.97f, 0.45f);
        previewText = MakeText(sideRt, "", 22, Ink, TextAnchor.UpperLeft, false);
        Anchor(previewText.rectTransform, 0.05f, 0.27f, 0.97f, 0.35f);
        var rule = MakeText(sideRt, "順番：属性 → 対象 → 威力 → 発射方法 → 追加効果（無くてもよい）\nメモリを使うほど処理速度が遅くなる", 19, UiKit.Hex("6a5a80"), TextAnchor.UpperLeft, false);
        Anchor(rule.rectTransform, 0.05f, 0.2f, 0.97f, 0.27f);
        programMessage = MakeText(sideRt, "", 22, Ink, TextAnchor.UpperLeft, true);
        Anchor(programMessage.rectTransform, 0.05f, 0.15f, 0.97f, 0.2f);

        var sendBorder = Img(sideRt, UiKit.RoundRect(14), Purple);
        sendBorder.type = Image.Type.Sliced;
        Anchor(sendBorder.rectTransform, 0.05f, 0.03f, 0.62f, 0.14f);
        var send = Img(sendBorder.rectTransform, UiKit.RoundRect(12), Color.white);
        send.type = Image.Type.Sliced;
        send.raycastTarget = true;
        Anchor(send.rectTransform, 0, 0, 1, 1, 4, 4, -4, -4);
        var sb = send.gameObject.AddComponent<Button>();
        sb.targetGraphic = send;
        sb.onClick.AddListener(OnSend);
        MakeText(send.rectTransform, "送信", 56, Ink, TextAnchor.MiddleCenter, false);
        var close = CommandButton(sideRt, "もどる", 30, () => { programScreen.SetActive(false); RefreshBattle(); }, false);
        Anchor((RectTransform)close.transform, 0.66f, 0.04f, 0.97f, 0.12f);

        programScreen.SetActive(false);
    }

    // スタート（帽子の形・緑の旗）とフィニッシュのブロック
    void MakeCapBlock(RectTransform parent, string label, string name, bool start)
    {
        var img = Img(parent, start ? UiKit.HatBlockShape() : UiKit.BlockShape(), StartColor);
        img.type = Image.Type.Sliced;
        var rt = img.rectTransform;
        rt.name = name;
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = start ? 84 : 66;
        if (start)
        {
            var pole = Img(rt, null, UiKit.Hex("2a8a3a")).rectTransform;
            Anchor(pole, 0, 0, 0, 0, 22, 18 + UiKit.BlockTabHeight, 25, 58 + UiKit.BlockTabHeight);
            var flag = Img(rt, null, UiKit.Hex("4ad04a")).rectTransform;
            Anchor(flag, 0, 0, 0, 0, 25, 40 + UiKit.BlockTabHeight, 50, 58 + UiKit.BlockTabHeight);
        }
        var t = MakeText(rt, label, 30, Color.white, TextAnchor.MiddleLeft, true);
        Anchor(t.rectTransform, 0, 0, 1, start ? 0.78f : 1, start ? 64 : 24, UiKit.BlockTabHeight, 0, 0);
        AddOutline(t, new Color(0.55f, 0.35f, 0f, 0.8f), 2f);
    }

    void MakeBlock(RectTransform parent, Block data, bool fromPalette)
    {
        var col = BlockColors[(int)data.Kind];
        var img = Img(parent, UiKit.BlockShape(), col);
        img.type = Image.Type.Sliced;
        var rect = img.rectTransform;
        rect.name = "Block";
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 66;
        var view = rect.gameObject.AddComponent<BlockView>();
        view.Init(this, data, fromPalette, canvas);
        // ▼ の付いた中身（クリックで変わる）
        var pill = Img(rect, UiKit.RoundRect(18), Color.Lerp(col, Color.black, 0.28f));
        pill.type = Image.Type.Sliced;
        pill.raycastTarget = true;
        Anchor(pill.rectTransform, 0, 0, 0, 1, 16, UiKit.BlockTabHeight + 8, 150, -8);
        var b = pill.gameObject.AddComponent<Button>();
        b.targetGraphic = pill;
        b.onClick.AddListener(() => OnBlockValueClicked(view));
        var valueText = MakeText(pill.rectTransform, "", 28, Color.white, TextAnchor.MiddleCenter, true);
        Anchor(valueText.rectTransform, 0, 0, 1, 1, 6, 0, -6, 0);
        var suffix = MakeText(rect, "", 24, Color.white, TextAnchor.MiddleLeft, true);
        Anchor(suffix.rectTransform, 0, 0, 1, 1, 160, UiKit.BlockTabHeight, 0, 0);
        view.SetLabels(valueText, suffix);
    }

    // ================= 部品 =================

    static RectTransform NewRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1, float l = 0, float b = 0, float r = 0, float t = 0)
    {
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(r, t);
    }

    static Image Img(Transform parent, Sprite sprite, Color color)
    {
        var rt = NewRect(parent, "Image");
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static RectTransform Box(Transform parent, Color color)
    {
        var rt = Img(parent, null, color).rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        return rt;
    }

    static void AddVertical(RectTransform rt, float spacing)
    {
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
    }

    Text MakeText(Transform parent, string s, int size, Color color, TextAnchor anchor, bool bold)
    {
        var rt = NewRect(parent, "Text");
        Fill(rt);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.text = s;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void AddOutline(Text t, Color color, float size)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(size, -size);
    }

    // 企画書の「行動コマンド」の形：黒い斜めの帯に水色のふち。main = true ならピンクの目立つ帯
    Button CommandButton(Transform parent, string label, int size, UnityEngine.Events.UnityAction onClick, bool main)
    {
        var rt = NewRect(parent, "Command");
        var edge = Img(rt, UiKit.Slant(), main ? UiKit.Hex("ffe0f4") : CommandEdge);
        edge.type = Image.Type.Sliced;
        Anchor(edge.rectTransform, 0, 0, 1, 1, 4, -4, 4, -4);
        var bgImg = Img(rt, UiKit.Slant(), main ? Pink : CommandBg);
        bgImg.type = Image.Type.Sliced;
        bgImg.raycastTarget = true;
        Fill(bgImg.rectTransform);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bgImg;
        var colors = b.colors;
        colors.highlightedColor = new Color(1f, 0.85f, 0.95f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.85f);
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
        b.colors = colors;
        b.onClick.AddListener(onClick);
        var t = MakeText(rt, label, size, main ? Color.white : UiKit.Hex("a8f4ff"), TextAnchor.MiddleLeft, true);
        Anchor(t.rectTransform, 0, 0, 1, 1, 30, 0, 0, 0);
        if (main) AddOutline(t, UiKit.Hex("8a1060"), 2f);
        return b;
    }
}

// 背景の演出などを、画面を切りかえても動かし続けるための入れ物
public class CoroutineRunner : MonoBehaviour { }

// ドラッグできるブロック。ドラッグ中は半透明の写しがマウスについてくる
public class BlockView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Block Data { get; private set; }
    public bool FromPalette { get; private set; }
    MagicGame game;
    Canvas canvas;
    RectTransform ghost;
    Text valueText, suffixText;

    public void Init(MagicGame g, Block data, bool fromPalette, Canvas c)
    {
        game = g;
        Data = data;
        FromPalette = fromPalette;
        canvas = c;
        GetComponent<Image>().raycastTarget = true;
    }

    public void SetLabels(Text value, Text suffix)
    {
        valueText = value;
        suffixText = suffix;
        suffixText.supportRichText = true;
        RefreshLabels();
    }

    // ▼ の中身と、右側の文字（かっこの中は使うメモリ）
    public void RefreshLabels()
    {
        valueText.text = Blocks.Label(Data.Kind, Data.Value) + " ▼";
        suffixText.text = Blocks.Suffix(Data.Kind) + "  <size=18>(" + Blocks.Memory(Data.Kind, Data.Value) + ")</size>";
    }

    public void OnBeginDrag(PointerEventData e)
    {
        var rt = (RectTransform)transform;
        var copy = Instantiate(gameObject, canvas.transform);
        Destroy(copy.GetComponent<BlockView>());
        var le = copy.GetComponent<LayoutElement>();
        if (le != null) le.ignoreLayout = true;
        ghost = (RectTransform)copy.transform;
        ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
        ghost.sizeDelta = rt.rect.size;
        var cg = copy.GetComponent<CanvasGroup>();
        if (cg == null) cg = copy.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.alpha = 0.85f;
        ghost.position = e.position;
        if (!FromPalette)
        {
            var mine = GetComponent<CanvasGroup>();
            if (mine == null) mine = gameObject.AddComponent<CanvasGroup>();
            mine.alpha = 0.3f;
        }
    }

    public void OnDrag(PointerEventData e)
    {
        if (ghost != null) ghost.position = e.position;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (ghost != null) Destroy(ghost.gameObject);
        ghost = null;
        var mine = GetComponent<CanvasGroup>();
        if (mine != null) mine.alpha = 1f;
        game.OnBlockDropped(this, e.position);
    }
}
