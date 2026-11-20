// マジカルコンパイルの画面と操作。シーンを再生すると自動で起動する（シーンに何も置かなくてよい）
// 世界観: プレイヤーはプログラマー。パソコンの中でプログラムを組んで魔法を作り、AI ロボットに送ってウイルスを撃退する
//
//   オープニング:   机の上のパソコンを 3D で映して画面に寄り、デスクトップへ（MagicGame.Intro.cs）
//   タイトル画面:   ロゴと3人。「スタート」でホームへ
//   ホーム:         依頼・キャラクター・ブロック図鑑・遊び方（MagicGame.Home.cs）
//   依頼一覧:       どのパソコン（依頼）を助けに行くか選ぶと戦闘へ
//   戦闘画面:       味方（左）とウイルス（右）が舞台に立つ。味方をクリックで選び、右のコマンドから
//                   「プログラム」で魔法を組む。「バトル開始」でタイムラインが進み、右端に着いたキャラから行動する
//                   ウイルスをクリックすると「1体」の魔法でねらう敵を選べる
//   プログラム画面: 左の手持ちブロックをドラッグして中央のコードに置く。コードの外へドラッグすると消える
//                   ブロックの ▼ の部分をクリックすると中身が変わる。「送信」でロボットに送る
//   チャージ:       そのターンは魔法を打たず、次のターンのメモリ容量と攻撃力が上がる
//   リンクプログラム: ペアのロボットを選び、2人分のメモリで1つの魔法を組む（1試合2回まで）。
//                   属性ブロック2つ・威力200/400・マジカルリンク（残りのメモリ全部で威力アップ）が使える
//
// 絵はすべてドット絵（ArtData.cs）。部品の形は UiKit.cs がコードで作る

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MagicBattle;

public partial class MagicGame : MonoBehaviour
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
        UiKit.Hex("4a8fe0"), UiKit.Hex("7c5cc9"), UiKit.Hex("2fae4a"), UiKit.Hex("e0408a"), UiKit.Hex("a39b3a"), UiKit.Hex("ff7a1a"),
    };
    static readonly Color ChargeColor = UiKit.Hex("ffd84a");
    static readonly Color StartColor = UiKit.Hex("f2b632");

    const float AllyScale = 6f;        // 戦闘画面のドット 1 つの大きさ
    static readonly RectInt FaceCrop = new RectInt(5, 1, 30, 27);   // 顔だけ切り出す範囲（上から数える）

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<MagicGame>() == null) new GameObject("MagicGame").AddComponent<MagicGame>();
    }

    enum Phase { Desktop, Title, Home, Requests, Planning, Running, Finished }

    Battle battle;
    Request currentRequest = Requests.All[0];
    Phase phase = Phase.Title;
    readonly Dictionary<Unit, List<Block>> drafts = new Dictionary<Unit, List<Block>>(); // 組みかけのコード
    // キャラごとに最後に組んだコード（次の戦闘でもそのまま使う）
    readonly Dictionary<string, List<Block>> savedDrafts = new Dictionary<string, List<Block>>();
    readonly Dictionary<Unit, List<Block>> linkDrafts = new Dictionary<Unit, List<Block>>(); // 組みかけのリンクプログラム
    bool linkMode;         // プログラム画面でリンクプログラムを組んでいる
    Unit linkPartner;      // そのときのペアの相手
    Unit selected;         // 選んでいる味方（コマンドの持ち主）
    Unit editing;          // プログラム画面で組んでいる味方
    bool busy;             // 行動のアニメーション中はタイムラインを止める
    int flying;            // 飛んでいる魔法の弾の数
    bool intro;            // ウイルスの登場演出中
    const float TimelineSpeed = 1.0f;

    Font font;
    Canvas canvas;
    RectTransform root;
    RectTransform titleLayer, battleLayer, stage, effects, noiseLayer, requestLayer, requestCards;
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
        public Image Aura;              // チャージ・リンクの光
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
    readonly Dictionary<Unit, (Text hp, Image fill, RectTransform box, Text status)> allyHud = new Dictionary<Unit, (Text, Image, RectTransform, Text)>();
    readonly Dictionary<Unit, RectTransform> icons = new Dictionary<Unit, RectTransform>();
    readonly Dictionary<Unit, Image> iconPartner = new Dictionary<Unit, Image>();   // リンクのとき 2 人目の顔
    Text turnText, logText, commandTitle;
    RectTransform commandMenu, timelineBar, pointerLine, hudPanel;
    readonly List<Button> commandButtons = new List<Button>();
    Text chargeLabel, linkLabel, goLabel;
    // コマンドバトル：このターンの行動を決めた味方（左から順に決めていき、全員そろったらバトル開始）
    readonly HashSet<Unit> decided = new HashSet<Unit>();
    GameObject linkPicker;
    RectTransform linkPickerList;
    GameObject resultPanel;
    Text resultText;
    readonly List<string> log = new List<string>();

    // プログラム画面
    GameObject programScreen;
    RectTransform codeArea, codeList, paletteList;
    Text memoryText, speedText, previewText, programMessage, bubbleText, robotName;
    Image memoryFill, robotPortrait, partnerPortrait, headerImg;
    Text headerText, paletteHint;
    RectTransform tabsRoot;
    GameObject cancelLinkButton;
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
        BuildRequests();
        BuildHome();
        BuildTeam();
        BuildLoadout();
        BuildBlockBook();
        BuildVirusBook();
        BuildMailbox();
        EnsureWelcomeMail();
        BuildDesktop();
        effects = Layer("Effects");
        BuildProgramScreen();
        BuildHowTo();
        BuildNoise();
        StartIntro();   // 3D のパソコン → デスクトップ → タイトル（MagicGame.Intro.cs）
    }

    void ShowTitle() => ShowTitle(true);

    void ShowTitle(bool withNoise)
    {
        StopAllCoroutines();
        phase = Phase.Title;
        desktopLayer.gameObject.SetActive(false);
        HideScreens();
        titleLayer.gameObject.SetActive(true);
        ClearChildren(effects);
        if (withNoise) StartCoroutine(Noise(0.4f, 0.5f));
    }

    void OnTitleStart()
    {
        if (phase != Phase.Title) return;
        ShowHome();
    }

    // 依頼一覧へ
    void ShowRequests()
    {
        StopAllCoroutines();
        ClearChildren(effects);
        phase = Phase.Requests;
        connecting = false;
        var old = requestLayer.Find("Connect");
        if (old != null) Destroy(old.gameObject);
        HideScreens();
        requestLayer.gameObject.SetActive(true);
        RebuildRequestCards();
        StartCoroutine(Noise(0.3f, 0.4f));
    }

    bool connecting;

    void OnRequestChosen(Request r)
    {
        if (phase != Phase.Requests || connecting) return;
        if (LoadParty().Count < Mathf.Min(3, Characters.All.Length))
        {
            StartCoroutine(Toast(requestLayer, "チーム編成で出撃メンバーを 3 人選んでください"));
            return;
        }
        currentRequest = r;
        StartCoroutine(Connect(r));
    }

    static bool IsCleared(Request r) => PlayerPrefs.GetInt("clear_" + r.Id, 0) == 1;

    void NewBattle()
    {
        StopAllCoroutines();
        ClearChildren(effects);
        var call = battleLayer.Find("Call");   // 前の依頼完了の通信が残っていたら消す
        if (call != null) Destroy(call.gameObject);
        for (int i = noiseLayer.childCount - 1; i >= 2; i--) Destroy(noiseLayer.GetChild(i).gameObject); // 走査線と砂嵐は残す
        noise.color = new Color(1, 1, 1, 0);
        busy = false;
        flying = 0;
        battle = new Battle(System.Environment.TickCount, currentRequest, LevelMap(), MemoryBonus, LoadoutMap(), LoadParty());
        drafts.Clear();
        linkDrafts.Clear();
        int carried = 0;
        foreach (var a in battle.Allies)
        {
            // 前に組んだコードがあればそのままセットし、送信できる形なら送信もしておく
            if (!savedDrafts.TryGetValue(a.Art, out var code)) { code = new List<Block>(); savedDrafts[a.Art] = code; }
            drafts[a] = code;
            if (code.Count > 0 && Compiler.Check(code, battle.CapacityOf(a), out _))
            {
                battle.SetSpell(a, Compiler.Build(code, battle.CapacityOf(a)));
                carried++;
            }
        }
        linkMode = false;
        linkPartner = null;
        selected = editing = battle.Allies[0];
        for (int i = 0; i < robotTabs.Count; i++)
        {
            var t = robotTabs[i];
            bool has = i < battle.Allies.Count;
            t.bg.gameObject.SetActive(has);
            if (!has) continue;
            robotTabs[i] = (battle.Allies[i], t.bg, t.text, t.face);
            t.face.sprite = UiKit.Art(battle.Allies[i].Art + "_idle", FaceCrop);
        }
        phase = Phase.Planning;
        log.Clear();
        AddLog(currentRequest.Client + "のパソコンにつないだ！ 左のロボットから順に行動を決めよう");
        if (carried > 0) AddLog("前に組んだプログラムを " + carried + " 人にセットした。「前と同じで決定」ですぐ決められる");
        if (currentRequest.Hint != "") AddLog("ヒント：" + currentRequest.Hint);
        StartCommandRound();
        BuildUnits();
        programScreen.SetActive(false);
        resultPanel.SetActive(false);
        linkPicker.SetActive(false);
        RefreshBattle();
        StartCoroutine(Intro());
    }

    // ================= 毎フレーム =================

    void Update()
    {
        if (phase == Phase.Running && !busy) StepBattle(Time.deltaTime);
        UpdateClock();
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
            foreach (var note in battle.EndTurn()) AddLog(note);
            StartCoroutine(TurnBanner(battle.Turn));
            phase = Phase.Planning;
            StartCommandRound();
            AddLog("ターン" + battle.Turn + "。左のロボットから行動を決めよう");
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
            // 行動するキャラだけ出す（チャージ・ガード・何もしない・リンクの相手は出さない）
            kv.Value.gameObject.SetActive((views.TryGetValue(u, out var v) ? !v.Dead : u.Alive) && u.OnTimeline);
            if (iconPartner.TryGetValue(u, out var face2))
            {
                // リンクしていれば、2 人の顔が並んだ横長のアイコンにする
                bool linked = u.LinkPartner != null;
                kv.Value.sizeDelta = new Vector2(linked ? 120 : 70, 70);
                kv.Value.GetComponent<Image>().color = linked ? Pink : Cyan;
                var face1 = kv.Value.GetComponentsInChildren<Image>(true);
                foreach (var f in face1)
                    if (f.rectTransform.name == "Face") Anchor(f.rectTransform, 0, 0, linked ? 0.5f : 1f, 1, 4, 4, linked ? 0 : -4, -4);
                face2.gameObject.SetActive(linked);
                if (linked && face2.sprite == null) face2.sprite = UiKit.Art(u.LinkPartner.Art + "_idle", FaceCrop);
                if (!linked) face2.sprite = null;
            }
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
        if (decided.Contains(u)) { AddLog(u.Name + "の行動は決定ずみです（選び直せません）"); return; }
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
        if (decided.Contains(selected)) { AddLog(selected.Name + "の行動は決定ずみです"); return; }
        if (selected.LinkedTo != null) { AddLog(selected.Name + "は" + selected.LinkedTo.Name + "とリンク中なので、プログラムを組めません"); return; }
        OpenProgram(selected, selected.LinkPartner);
    }

    // プログラム画面を開く。partner があればリンクプログラム
    void OpenProgram(Unit main, Unit partner)
    {
        linkPicker.SetActive(false);
        editing = main;
        linkPartner = partner;
        linkMode = partner != null;
        if (linkMode && !linkDrafts.ContainsKey(main)) linkDrafts[main] = new List<Block>();
        RebuildPalette();
        programScreen.SetActive(true);
        RefreshProgram();
        StartCoroutine(Noise(0.2f, linkMode ? 0.5f : 0.3f));
    }

    // チャージ：そのターンは魔法を打たず、次のターンにパワーアップ
    void OnChargeCommand()
    {
        if (phase != Phase.Planning || intro || selected == null || !selected.Alive) return;
        if (decided.Contains(selected)) { AddLog(selected.Name + "の行動は決定ずみです"); return; }
        if (selected.LinkPartner != null || selected.LinkedTo != null) { AddLog("リンク中はチャージできません"); return; }
        bool on = !selected.Charging;
        battle.SetCharge(selected, on);
        AddLog(on ? selected.Name + "はこのターン、チャージする（次のターン メモリ容量と攻撃力がアップ）" : selected.Name + "のチャージをやめた");
        if (on) { StartCoroutine(ChargeFx(views[selected], 0.5f)); Decide(selected); return; }
        decided.Remove(selected);
        RefreshBattle();
    }

    // ガード：このターンは攻撃しない代わりに、受けるダメージが半分になる
    void OnGuardCommand()
    {
        if (phase != Phase.Planning || intro || selected == null || !selected.Alive) return;
        if (decided.Contains(selected)) { AddLog(selected.Name + "の行動は決定ずみです"); return; }
        if (selected.LinkPartner != null || selected.LinkedTo != null) { AddLog("リンク中はガードできません"); return; }
        battle.SetGuard(selected);
        AddLog(selected.Name + "はこのターン、ガードする（受けるダメージ半分）");
        StartCoroutine(GuardFx(views[selected]));
        Decide(selected);
    }

    // ガードの光（水色の盾が一瞬光る）
    IEnumerator GuardFx(UnitView v)
    {
        var shield = Img(effects, UiKit.Circle(), new Color(0.4f, 0.9f, 1f, 0.5f)).rectTransform;
        shield.sizeDelta = new Vector2(v.Size.x * 1.1f, v.Size.y * 0.9f);
        shield.position = v.Art.TransformPoint(new Vector3(0, v.Size.y * 0.5f, 0));
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f)
        {
            if (shield == null) yield break;
            shield.localScale = Vector3.one * (0.8f + 0.3f * t);
            shield.GetComponent<Image>().color = new Color(0.4f, 0.9f, 1f, 0.5f * (1f - t));
            yield return null;
        }
        if (shield != null) Destroy(shield.gameObject);
    }

    // リンクプログラム：ペアになるロボットを選ぶ
    void OnLinkCommand()
    {
        if (phase != Phase.Planning || intro || selected == null || !selected.Alive) return;
        if (decided.Contains(selected)) { AddLog(selected.Name + "の行動は決定ずみです"); return; }
        if (selected.LinkedTo != null) { AddLog(selected.Name + "は" + selected.LinkedTo.Name + "とリンク中です"); return; }
        if (selected.LinkPartner != null) { OpenProgram(selected, selected.LinkPartner); return; }
        if (battle.LinksLeft <= 0) { AddLog("リンクプログラムはこの試合ではもう使えません"); return; }
        ClearChildren(linkPickerList);
        foreach (var u in battle.Allies)
        {
            if (u == selected || !u.Alive || u.LinkPartner != null || u.LinkedTo != null || decided.Contains(u)) continue;   // 決定ずみの味方はペアにできない
            var partner = u;
            var b = CommandButton(linkPickerList, "", 34, () => OpenProgram(selected, partner), false);
            b.gameObject.AddComponent<LayoutElement>().preferredHeight = 90;
            var face = Img(b.transform, UiKit.Art(u.Art + "_idle", FaceCrop), Color.white);
            face.preserveAspect = true;
            Anchor(face.rectTransform, 0, 0, 0, 1, 24, 4, 110, -4);
            var t = MakeText(b.transform, u.Name + "　<size=22>" + u.TraitName() + "</size>", 34, UiKit.Hex("a8f4ff"), TextAnchor.MiddleLeft, true);
            t.supportRichText = true;
            Anchor(t.rectTransform, 0, 0, 1, 1, 124, 0, 0, 0);
        }
        if (linkPickerList.childCount == 0) { AddLog("ペアになれるロボットがいません"); return; }
        linkPicker.SetActive(true);
    }

    void OnCancelLink()
    {
        if (editing == null || editing.LinkPartner == null) return;
        AddLog(editing.Name + "と" + editing.LinkPartner.Name + "のリンクをやめた");
        decided.Remove(editing);
        if (editing.LinkPartner != null) decided.Remove(editing.LinkPartner);
        battle.CancelLink(editing);
        programScreen.SetActive(false);
        RefreshBattle();
    }

    void OnNotReady(string name)
    {
        AddLog(name + "はまだ準備中です");
    }

    int ProgramCapacity() => linkMode ? battle.LinkCapacity(editing, linkPartner) : battle.CapacityOf(editing);
    List<Block> CurrentCode() => linkMode ? linkDrafts[editing] : drafts[editing];

    // 魔法の処理速度の表示に使うメモリ容量
    int SpellCapacity(Unit u) => u.Spell != null && u.Spell.IsLink && u.LinkPartner != null ? battle.LinkCapacity(u, u.LinkPartner) : battle.CapacityOf(u);

    // ---- コマンドバトルの順番 ----

    // ターンの始め：だれも決めていない状態で、いちばん左の味方から
    void StartCommandRound()
    {
        decided.Clear();
        selected = null;
        NextCommand();
    }

    bool AllDecided()
    {
        foreach (var a in battle.Allies)
            if (a.Alive && a.LinkedTo == null && !decided.Contains(a)) return false;
        return true;
    }

    // 次に行動を決める味方を選ぶ（左から。リンクの相手は決めなくてよい）。全員決まったら選ばない
    void NextCommand()
    {
        int start = selected == null ? 0 : battle.Allies.IndexOf(selected) + 1;
        for (int k = 0; k < battle.Allies.Count; k++)
        {
            var a = battle.Allies[(start + k) % battle.Allies.Count];
            if (a.Alive && a.LinkedTo == null && !decided.Contains(a)) { selected = a; return; }
        }
        if (selected == null || !selected.Alive) selected = battle.Allies.Find(a => a.Alive);
    }

    // この味方の行動が決まった → 次の味方へ
    void Decide(Unit u)
    {
        decided.Add(u);
        if (u.LinkPartner != null) decided.Add(u.LinkPartner);
        NextCommand();
        if (AllDecided())
        {
            AddLog("全員の行動が決まった！ 戦闘開始！");
            RefreshBattle();
            StartCoroutine(AutoStart());
            return;
        }
        AddLog(selected.Name + "の行動を選んでください");
        RefreshBattle();
    }

    // 全員の行動が決まったら、少し間をおいて自動で戦闘を始める
    IEnumerator AutoStart()
    {
        yield return new WaitForSeconds(0.5f);
        if (phase == Phase.Planning && !intro && AllDecided()) OnStartButton();
    }

    // ピンクのボタン：まだ決めていない人がいれば「前と同じで決定」、全員決まったら「バトル開始」
    void OnGoButton()
    {
        if (phase != Phase.Planning || intro) return;
        if (AllDecided()) { OnStartButton(); return; }
        if (selected == null) return;
        if (selected.Spell == null && !selected.Charging) AddLog(selected.Name + "はプログラムが無いので、このターンは何もしない");
        Decide(selected);
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
        var code = CurrentCode();
        int cap = ProgramCapacity();
        if (!Compiler.Check(code, cap, out string msg, linkMode))
        {
            programMessage.text = "送信できません：" + msg;
            programMessage.color = UiKit.Hex("e0284a");
            StartCoroutine(Shake(programMessage.rectTransform));
            return;
        }
        var spell = Compiler.Build(code, cap);
        if (linkMode)
        {
            if (!battle.Link(editing, linkPartner, spell))
            {
                programMessage.text = "送信できません：リンクプログラムはもう使えません";
                programMessage.color = UiKit.Hex("e0284a");
                return;
            }
            StartCoroutine(Noise(0.4f, 0.6f));
        }
        else battle.SetSpell(editing, spell);
        programMessage.text = "送信しました！ " + editing.Name + (linkMode ? "＆" + linkPartner.Name : "") + "：" + spell.Describe();
        programMessage.color = UiKit.Hex("1e9a4a");
        AddLog(editing.Name + "にプログラムを送信（" + spell.Describe() + "）");
        RefreshBattle();
        RefreshProgram(keepMessage: true);
        StartCoroutine(Noise(0.15f, 0.25f));
        StartCoroutine(AfterSend(editing));
    }

    // 送信したら少しして画面を閉じ、次の味方の行動選択へ
    IEnumerator AfterSend(Unit who)
    {
        yield return new WaitForSeconds(0.45f);
        if (!programScreen.activeSelf || phase != Phase.Planning) yield break;
        programScreen.SetActive(false);
        selected = who;
        Decide(who);
    }

    // ================= ブロックのドラッグ（BlockView から呼ばれる） =================

    public void OnBlockDropped(BlockView view, Vector2 screenPos)
    {
        var code = CurrentCode();
        bool overCode = RectTransformUtility.RectangleContainsScreenPoint(codeArea, screenPos, null);
        // 置く場所：マウスより上にあるコードの行の数（はじめに数える）
        int k = 0;
        foreach (Transform row in codeList)
        {
            var bv = row.GetComponentInChildren<BlockView>();
            if (bv == null) continue;
            if (((RectTransform)row).position.y > screenPos.y) k++;
        }
        k = Mathf.Min(k, code.Count);

        if (view.FromPalette)
        {
            if (!overCode) { RefreshProgram(); return; }
            var group = new List<Block> { view.Data.Clone() };
            group[0].WithElse = false;
            if (view.Data.Kind == BlockKind.If)
            {
                if (view.Data.WithElse) group.Add(new Block(BlockKind.Else));
                group.Add(new Block(BlockKind.EndIf));
            }
            if (view.Data.Kind == BlockKind.Repeat) group.Add(new Block(BlockKind.EndRepeat));
            code.InsertRange(k, group);
        }
        else
        {
            // 「もし」「繰り返す」は中のブロックごと動かす
            int a = code.IndexOf(view.Data);
            if (a < 0) { RefreshProgram(); return; }
            int b = GroupEnd(code, a);
            var group = code.GetRange(a, b - a + 1);
            if (!overCode) code.RemoveRange(a, b - a + 1);           // コードの外へ出したら消す
            else if (k < a || k > b + 1)
            {
                code.RemoveRange(a, b - a + 1);
                if (k > b) k -= group.Count;
                code.InsertRange(k, group);
            }
        }
        RefreshProgram();
    }

    // 「もし」「繰り返す」なら対応する「おわり」の場所。ふつうのブロックはそのまま
    static int GroupEnd(List<Block> code, int a)
    {
        var kind = code[a].Kind;
        if (kind != BlockKind.If && kind != BlockKind.Repeat) return a;
        int depth = 0;
        for (int i = a; i < code.Count; i++)
        {
            var kk = code[i].Kind;
            if (kk == BlockKind.If || kk == BlockKind.Repeat) depth++;
            if (kk == BlockKind.EndIf || kk == BlockKind.EndRepeat) { depth--; if (depth == 0) return i; }
        }
        return code.Count - 1;
    }

    public void OnBlockValueClicked(BlockView view)
    {
        view.Data.Value = (view.Data.Value + 1) % Blocks.OptionCount(view.Data.Kind, linkMode);
        view.RefreshLabels();
        if (!view.FromPalette) RefreshProgram();
    }

    // ================= 表示の更新 =================

    void RefreshBattle()
    {
        turnText.text = "ターン" + battle.Turn + (battle.WaveCount > 1 ? "<size=30>　WAVE " + (battle.Wave + 1) + "/" + battle.WaveCount + "</size>" : "");
        turnText.supportRichText = true;
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
                hud.status.text = (decided.Contains(u) && phase == Phase.Planning ? "<color=#7dff8a>決定</color>　" : "") + StatusOf(u);
                hud.status.supportRichText = true;
                v.Aura.gameObject.SetActive(!v.Dead && (u.Charging || u.Charged || u.LinkPartner != null || u.LinkedTo != null));
                v.Aura.color = u.LinkPartner != null || u.LinkedTo != null ? new Color(1f, 0.4f, 0.85f, 0.6f) : u.Charged ? new Color(1f, 0.85f, 0.2f, 0.75f) : new Color(1f, 0.85f, 0.2f, 0.4f);
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
        {
            string st = StatusOf(selected);
            commandTitle.text = selected.Name + "　<size=22>" + selected.TraitName() + (st != "" ? "　[" + st + "]" : "") + "</size>\n<size=22>"
                + (selected.LinkedTo != null ? selected.LinkedTo.Name + "にメモリを貸している"
                   : selected.Charging ? "このターンはチャージ"
                   : selected.Guarding ? "このターンはガード（ダメージ半分）"
                   : selected.Spell == null ? "プログラム：まだ無い"
                   : selected.Spell.Describe() + "（" + Compiler.SpeedLabel(selected.Spell.Memory, SpellCapacity(selected)) + "）") + "</size>";
            chargeLabel.text = selected.Charging ? "チャージをやめる" : "チャージ";
            int order = battle.Allies.IndexOf(selected) + 1;
            commandTitle.text = "<size=22><color=#ffd84a>" + order + "人目の行動" + (decided.Contains(selected) ? "（決定ずみ）" : "") + "</color></size>\n" + commandTitle.text;
        }
        goLabel.text = AllDecided() ? "バトル開始" : selected != null && selected.Spell != null ? "前と同じで決定" : "何もしないで決定";
        linkLabel.text = "リンクプログラム <size=24>残り" + battle.LinksLeft + "</size>";
        foreach (var b in commandButtons) b.interactable = plan;
    }

    // 味方の状態（HP の下と、コマンドの見出しに出す）
    static string StatusOf(Unit u)
    {
        if (u.LinkPartner != null) return "リンク：" + u.LinkPartner.Name;
        if (u.LinkedTo != null) return "リンク：" + u.LinkedTo.Name;
        if (u.Charging) return "チャージ中";
        if (u.Guarding) return "ガード中";
        if (u.Locked) return "ロック中";
        if (u.Charged) return "パワーアップ";
        if (u.Spell != null) return "送信済み";
        return "";
    }

    // 手持ちブロックを並べ直す（リンクのときはマジカルリンクのブロックが増える）
    void RebuildPalette()
    {
        ClearChildren(paletteList);
        foreach (var k in linkMode ? Blocks.LinkOrder : Blocks.Order) MakeBlock(paletteList, new Block(k), true);
        // 装備した追加ブロック（リンクのときは2人分）
        // （起動したときはまだだれも選んでいないので、editing が無いこともある）
        bool hasIf = editing != null && (editing.HasMod(Modules.If) || (linkMode && linkPartner != null && linkPartner.HasMod(Modules.If)));
        bool hasRepeat = editing != null && (editing.HasMod(Modules.Repeat) || (linkMode && linkPartner != null && linkPartner.HasMod(Modules.Repeat)));
        if (hasIf)
        {
            MakeBlock(paletteList, new Block(BlockKind.If), true);
            MakeBlock(paletteList, new Block(BlockKind.If) { WithElse = true }, true);
        }
        if (hasRepeat) MakeBlock(paletteList, new Block(BlockKind.Repeat), true);
        paletteHint = MakeText(paletteList, linkMode
            ? "リンク：2人分のメモリでふつうに組むか、マジカルリンク（メモリを全部使うコンビ魔法。それ1つで完成）を置こう"
            : "ドラッグして右のコードへ。\n▼ をクリックで中身が変わる", 20, Ink, TextAnchor.UpperLeft, false);
        paletteHint.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
    }

    void RefreshProgram(bool keepMessage = false)
    {
        var code = CurrentCode();
        int cap = ProgramCapacity();
        tabsRoot.gameObject.SetActive(!linkMode);
        headerImg.color = linkMode ? Pink : UiKit.Hex("1e5cf0");
        headerText.text = linkMode ? "リンクプログラム" : "プログラム";
        cancelLinkButton.SetActive(false);   // 決めた行動は選び直せないので、送ったリンクも取り消さない
        partnerPortrait.gameObject.SetActive(linkMode);
        if (linkMode) partnerPortrait.sprite = UiKit.Art(linkPartner.Art + "_idle", new RectInt(0, 0, 40, 44));
        foreach (var t in robotTabs)
        {
            if (t.unit == null || !t.bg.gameObject.activeSelf) continue;
            bool sel = t.unit == editing;
            t.bg.color = sel ? Pink : new Color(1, 1, 1, 0.85f);
            t.text.color = sel ? Color.white : Ink;
            t.text.text = t.unit.Name + (t.unit.Spell != null ? "\n<size=18>送信済み</size>" : "") + (t.unit.Alive ? "" : "\n<size=18>戦闘不能</size>");
        }
        robotName.text = linkMode ? editing.Name + "＋" + linkPartner.Name : editing.Name;
        robotPortrait.sprite = UiKit.Art(editing.Art + "_idle", new RectInt(0, 0, 40, 44));
        bubbleText.text = linkMode ? editing.TraitName() + "\n＋\n" + linkPartner.TraitName() : editing.TraitName().Replace(" ", "\n");
        bubbleText.fontSize = linkMode ? 26 : 36;

        // コードを並べ直す（スタートとフィニッシュはそのまま）
        for (int i = codeList.childCount - 1; i >= 0; i--)
        {
            var ch = codeList.GetChild(i);
            if (ch.name == "Start" || ch.name == "Finish") continue;
            ch.SetParent(null, false);   // すぐに外す（Destroy はフレームの終わりなので、残ったまま数えないように）
            Destroy(ch.gameObject);
        }
        var finish = codeList.Find("Finish");
        BuildCodeRows(code);
        if (finish != null) finish.SetAsLastSibling();

        int used = Compiler.MemoryUsed(code, cap);
        memoryText.text = used + " / " + cap + (editing.Charged ? " <size=22>チャージ+30%</size>" : "");
        memoryText.supportRichText = true;
        memoryFill.fillAmount = Mathf.Clamp01((float)used / cap);
        memoryFill.color = used > cap ? UiKit.Hex("ff3b5c") : linkMode ? Pink : UiKit.Hex("3fb8ff");
        string sp = Compiler.SpeedLabel(used, cap);
        speedText.text = sp;
        speedText.color = sp == "速い" ? UiKit.Hex("2a7de8") : sp == "ふつう" ? UiKit.Hex("2a9a4a") : UiKit.Hex("e0284a");

        bool ok = Compiler.Check(code, cap, out string msg, linkMode);
        if (ok)
        {
            var s = Compiler.Build(code, cap);
            s.Partner = linkPartner;
            if (linkMode) Compiler.ApplyLinkElements(s, editing, linkPartner);
            var t = battle.CurrentTarget();
            int dmg = Battle.Damage(editing, s, t);
            float eff = Battle.Effectiveness(s, t);
            previewText.text = "見込み：1発 " + dmg + " ダメージ × " + s.Shots + "回" + (s.Target == TargetType.All ? "（敵全員）" : "（" + (t != null ? t.Name : "-") + "）")
                + (eff > 1.01f ? "　<color=#d08000>弱点をつける！</color>" : eff < 0.99f ? "　<color=#7a80a0>効きにくい…</color>" : "");
            previewText.supportRichText = true;
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
            if (v.Aura != null && v.Aura.gameObject.activeSelf)
                v.Aura.rectTransform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t * 5f + v.Phase));
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

        if (r.Actor.IsAlly && r.Actor.LinkedTo != null)
        {
            // リンクの相手：メモリを貸している（ピンクの光）
            Burst(actor.Art.TransformPoint(new Vector3(0, actor.Size.y * 0.5f, 0)), Pink, 10, 10f, 160f);
            yield return Lunge(actor, 1f, 8f);
        }
        else if (r.Actor.IsAlly && r.Actor.Charging)
        {
            yield return ChargeFx(actor, 0.8f);
        }
        else if (r.Actor.IsAlly && r.Actor.Spell != null)
        {
            var s = r.Actor.Spell;
            var color = ElementColor(s.Element);
            var color2 = s.HasElement2 ? ElementColor(s.Element2) : color;
            // 詠唱：魔法のポーズ、手の前に魔法陣。リンクなら相手も一緒に
            actor.ArtImage.sprite = actor.Cast;
            actor.PoseTimer = 1.6f;
            RectTransform circle2 = null;
            UnitView pv = null;
            if (s.Partner != null) views.TryGetValue(s.Partner, out pv);
            if (s.IsLink && pv != null && !pv.Dead)
            {
                pv.ArtImage.sprite = pv.Cast;
                pv.PoseTimer = 1.6f;
                circle2 = MagicCircleAt(pv, color2);
                StartCoroutine(Lunge(pv, 1f, 18f));
                StartCoroutine(Noise(0.4f, 0.5f));
                AddLog("マジカルリンク！");
            }
            var circle = MagicCircleAt(actor, color);
            if (s.IsLink) { circle.sizeDelta *= 1.5f; StartCoroutine(ChargeFx(actor, 0.4f)); }
            yield return ChargeUp(actor, color, s.IsLink ? 0.45f : 0.28f);   // 手の先に光が集まる
            yield return Lunge(actor, 1f, 18f);
            bool big = (s.HasEffect && s.Effect == Effect.Blast) || s.IsLink;
            int n = 0;
            foreach (var h in r.Hits)
            {
                if (h.Heal) continue;
                StartCoroutine(Beam(actor, h, s.Element, n++ % 2 == 0 ? color : color2, color2, big, s.HasEffect && s.Effect == Effect.Slow, s.IsLink));
                yield return new WaitForSeconds(0.16f);
            }
            if (circle2 != null) Destroy(circle2.gameObject);
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
        if (phase == Phase.Running && battle.WaveCleared && !battle.Lost)
        {
            yield return NextWaveSequence();
            busy = false;
            yield break;
        }
        busy = false;
        if (phase == Phase.Running && (battle.Won || battle.Lost))
        {
            phase = Phase.Finished;
            AddLog(battle.Won ? "ウイルスを全部駆除した！" : "システムダウン…");
            if (battle.Won) StartCoroutine(Victory());   // 依頼完了の通信（MagicGame.Progress.cs）
            else StartCoroutine(ShowResult(false));
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

    // チャージ：足もとから黄色い光が立ちのぼる
    IEnumerator ChargeFx(UnitView v, float dur)
    {
        float spawn = 0f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            if (v.Art == null) yield break;
            spawn -= Time.deltaTime;
            if (spawn <= 0f)
            {
                spawn = 0.03f;
                var p = v.Art.TransformPoint(new Vector3(Random.Range(-0.4f, 0.4f) * v.Size.x, Random.Range(0f, 0.3f) * v.Size.y, 0));
                var sq = Box(effects, ChargeColor);
                sq.sizeDelta = Vector2.one * Random.Range(8f, 16f);
                sq.position = p;
                StartCoroutine(Spark(sq, new Vector2(Random.Range(-20f, 20f), Random.Range(180f, 320f))));
            }
            v.ArtImage.color = Color.Lerp(Color.white, new Color(1f, 0.95f, 0.6f), 0.5f + 0.5f * Mathf.Sin(t * 20f));
            yield return null;
        }
        if (v.ArtImage != null) v.ArtImage.color = Color.white;
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

    // ---- 魔法のビーム ----

    Vector3 HandPoint(UnitView v) => v.Art.TransformPoint(new Vector3(v.Size.x * 0.5f, v.Size.y * 0.42f, 0));

    // ため：手の先に、まわりから光の粒が集まってくる
    IEnumerator ChargeUp(UnitView v, Color color, float dur)
    {
        Vector2 hand = effects.InverseTransformPoint(HandPoint(v));
        var dots = new List<(RectTransform rt, Vector2 from)>();
        for (int i = 0; i < 14; i++)
        {
            var d = Img(effects, UiKit.Glow(), Color.Lerp(color, Color.white, 0.4f)).rectTransform;
            d.anchorMin = d.anchorMax = new Vector2(0.5f, 0.5f);
            d.sizeDelta = Vector2.one * Random.Range(14f, 26f);
            var from = hand + Random.insideUnitCircle.normalized * Random.Range(90f, 160f);
            d.anchoredPosition = from;
            dots.Add((d, from));
        }
        var glow = Img(effects, UiKit.Glow(), new Color(color.r, color.g, color.b, 0.9f)).rectTransform;
        glow.anchorMin = glow.anchorMax = new Vector2(0.5f, 0.5f);
        glow.anchoredPosition = hand;
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            float e = t * t;
            foreach (var d in dots) if (d.rt != null) d.rt.anchoredPosition = Vector2.Lerp(d.from, hand, e);
            glow.sizeDelta = Vector2.one * (30f + 70f * t);
            yield return null;
        }
        foreach (var d in dots) if (d.rt != null) Destroy(d.rt.gameObject);
        Destroy(glow.gameObject);
    }

    // ビーム：手の先から敵へ一直線に光線が伸び、当たったら光の輪と火花。白い芯・属性の色・外側の光の3重
    IEnumerator Beam(UnitView from, Hit h, Element element, Color color, Color color2, bool big, bool slow, bool twin)
    {
        flying++;
        views.TryGetValue(h.Target, out UnitView tv);
        Vector2 a = effects.InverseTransformPoint(HandPoint(from));
        Vector2 b = tv != null ? (Vector2)effects.InverseTransformPoint(tv.Art.TransformPoint(new Vector3(0, tv.Size.y * 0.5f, 0))) : a;
        float dist = Vector2.Distance(a, b);
        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        float thick = big ? 54f : 36f;

        var root = NewRect(effects, "Beam");
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0f, 0.5f);
        root.anchoredPosition = a;
        root.localRotation = Quaternion.Euler(0, 0, angle);
        var outer = BeamLayer(root, new Color(color.r, color.g, color.b, 0.35f), thick * 1.6f);
        var mid = BeamLayer(root, color, thick);
        RectTransform second = twin ? BeamLayer(root, new Color(color2.r, color2.g, color2.b, 0.9f), thick * 0.45f) : null;
        var core = BeamLayer(root, Color.white, thick * 0.3f);

        // 伸びる
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.07f)
        {
            root.sizeDelta = new Vector2(dist * t, thick);
            yield return null;
        }
        root.sizeDelta = new Vector2(dist, thick);
        Impact(h, color, big);
        Explosion(b, element, color, big);
        if (slow && tv != null) Burst(effects.TransformPoint(b), new Color(0.6f, 0.6f, 1f), 8, 12f, 140f);
        StartCoroutine(ShakeStage(big ? 12f : 7f));

        // ゆらめく
        for (float t = 0f; t < 0.2f; t += Time.deltaTime)
        {
            float k = 1f + 0.25f * Mathf.Sin(Time.time * 70f);
            mid.sizeDelta = new Vector2(0, thick * k);
            core.sizeDelta = new Vector2(0, thick * 0.3f * k);
            if (second != null) second.anchoredPosition = new Vector2(0, Mathf.Sin(Time.time * 40f) * thick * 0.35f);
            yield return null;
        }
        // 細くなって消える
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.15f)
        {
            float k = 1f - t;
            outer.sizeDelta = new Vector2(0, thick * 1.6f * k);
            mid.sizeDelta = new Vector2(0, thick * k);
            core.sizeDelta = new Vector2(0, thick * 0.3f * k);
            if (second != null) second.sizeDelta = new Vector2(0, thick * 0.45f * k);
            yield return null;
        }
        Destroy(root.gameObject);
        flying--;
    }

    // ビームの 1 枚（横に伸びる角の丸い帯）
    RectTransform BeamLayer(RectTransform root, Color c, float height)
    {
        var img = Img(root, UiKit.RoundRect(16), c);
        img.type = Image.Type.Sliced;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.sizeDelta = new Vector2(0, height);
        return rt;
    }

    // 当たったところ：大きな光・広がる光の輪・属性ごとの火花
    void Explosion(Vector2 at, Element element, Color color, bool big)
    {
        var world = effects.TransformPoint(at);
        StartCoroutine(Flash(at, Color.white, big ? 260f : 180f, 0.18f));
        StartCoroutine(RingOut(at, color, big ? 420f : 280f));
        if (element == Element.Fire)
        {
            Burst(world, UiKit.Hex("ffd84a"), big ? 18 : 10, 14f, 360f);
            Burst(world, UiKit.Hex("ff5a1a"), big ? 14 : 8, 18f, 260f);
        }
        else if (element == Element.Ice)
        {
            Burst(world, Color.white, big ? 16 : 9, 12f, 320f);
            Burst(world, UiKit.Hex("7fe4ff"), big ? 16 : 9, 16f, 240f);
        }
        else
        {
            Burst(world, UiKit.Hex("e8d8ff"), big ? 18 : 10, 12f, 340f);
            Burst(world, UiKit.Hex("b07aff"), big ? 12 : 7, 16f, 240f);
        }
    }

    IEnumerator Flash(Vector2 at, Color color, float size, float dur)
    {
        var f = Img(effects, UiKit.Glow(), color).rectTransform;
        f.anchorMin = f.anchorMax = new Vector2(0.5f, 0.5f);
        f.anchoredPosition = at;
        var img = f.GetComponent<Image>();
        for (float t = 0f; t < 1f; t += Time.deltaTime / dur)
        {
            f.sizeDelta = Vector2.one * size * (0.6f + 0.6f * t);
            img.color = new Color(color.r, color.g, color.b, 1f - t);
            yield return null;
        }
        Destroy(f.gameObject);
    }

    IEnumerator RingOut(Vector2 at, Color color, float size)
    {
        var r = Img(effects, UiKit.Ring(), color).rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = at;
        var img = r.GetComponent<Image>();
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            float e = 1f - (1f - t) * (1f - t);
            r.sizeDelta = Vector2.one * size * (0.15f + e);
            img.color = new Color(color.r, color.g, color.b, 1f - t);
            yield return null;
        }
        Destroy(r.gameObject);
    }

    // 画面（舞台）が少しゆれる
    IEnumerator ShakeStage(float power)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f)
        {
            stage.anchoredPosition = Random.insideUnitCircle * power * (1f - t);
            yield return null;
        }
        stage.anchoredPosition = Vector2.zero;
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

    // 敵の弱点・耐性・特性を 2 行で
    static string VirusTraits(Unit u)
    {
        return "<color=#ffd84a>弱点 " + Elements(u.Weak) + "</color>　<color=#a8b0c8>耐性 " + Elements(u.Resist) + "</color>\n"
             + "<color=#ff8ae0>特性 " + Unit.AbilityName(u.Ability) + "</color>";
    }

    static string Elements(Element[] es)
    {
        if (es == null || es.Length == 0) return "なし";
        var parts = new List<string>();
        foreach (var e in es) parts.Add(Blocks.Label(BlockKind.Element, (int)e));
        return string.Join("・", parts);
    }

    void ShowPopup(Hit h, Vector3 worldPos)
    {
        if (h.Weak || h.Resist)
        {
            // 弱点をついた／効きにくかった
            var tag = MakeText(effects, h.Weak ? "WEAK!" : "いまひとつ…", h.Weak ? 40 : 30, h.Weak ? UiKit.Hex("ffd84a") : UiKit.Hex("b8c0d8"), TextAnchor.MiddleCenter, true);
            tag.fontStyle = FontStyle.BoldAndItalic;
            AddOutline(tag, Color.black, 3f);
            var trt = tag.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(300, 60);
            trt.position = worldPos;
            trt.anchoredPosition += new Vector2(0, 60);
            StartCoroutine(Popup(trt, tag));
        }
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
        resultText.text = won ? "ウイルス駆除 完了！\n<size=40>" + currentRequest.Client + "のパソコンを助けた！</size>" : "システムダウン…";
        resultText.supportRichText = true;
        resultText.color = won ? Cyan : Magenta;
        StartCoroutine(Noise(0.5f, 0.6f));
    }

    // 「○ターン目」の大きな帯：横からスライドして入り、少し止まって反対へ抜ける
    IEnumerator TurnBanner(int turn)
    {
        yield return Banner(turn + "ターン目", turn == 1 ? "プログラムを組んで「バトル開始」！" : "プログラムはそのまま。組み直してもOK", UiKit.Hex("1e5cf0"));
    }

    // 強敵戦：ウェーブを全部倒したら、次のウイルスが奥から出てくる
    IEnumerator NextWaveSequence()
    {
        yield return Banner("WAVE CLEAR!", "奥からなにか来る…", UiKit.Hex("2fae4a"));
        var notes = battle.NextWave();
        foreach (var n in notes) AddLog(n);
        phase = Phase.Planning;
        StartCommandRound();
        BuildUnits();
        foreach (var e in battle.Enemies) { var v = views[e]; v.ArtImage.enabled = false; v.Dying = true; }
        intro = true;
        RefreshBattle();
        bool last = battle.Wave + 1 == battle.WaveCount;
        StartCoroutine(Noise(0.8f, 0.7f));
        yield return Banner("WAVE " + (battle.Wave + 1) + " / " + battle.WaveCount, last ? "親玉が出てきた！" : "新しいウイルスが出てきた！", Magenta);
        foreach (var e in battle.Enemies)
        {
            StartCoroutine(Noise(0.3f, 0.45f));
            yield return Materialize(views[e]);
        }
        intro = false;
        RefreshBattle();
        StartCoroutine(TurnBanner(battle.Turn));
    }

    IEnumerator Banner(string main, string subText, Color outline)
    {
        var band = Img(effects, UiKit.Slant(), new Color(0.03f, 0.02f, 0.08f, 0.85f));
        band.type = Image.Type.Sliced;
        var b = band.rectTransform;
        Anchor(b, -0.05f, 0.47f, 1.05f, 0.65f);
        var stripeTop = Img(b, null, Pink).rectTransform;
        Anchor(stripeTop, 0, 1, 1, 1, 0, -6, 0, 0);
        var stripeBottom = Img(b, null, Cyan).rectTransform;
        Anchor(stripeBottom, 0, 0, 1, 0, 0, 0, 0, 6);
        var textRoot = NewRect(b, "Text");
        Fill(textRoot);
        var t = MakeText(textRoot, main, 120, Color.white, TextAnchor.MiddleCenter, true);
        t.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(t, outline, 5f);
        var sub = MakeText(textRoot, subText, 30, Cyan, TextAnchor.LowerCenter, true);
        Anchor(sub.rectTransform, 0, 0, 1, 1, 0, 14, 0, 0);

        // 帯が開く
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.12f) { b.localScale = new Vector3(1f, k, 1f); yield return null; }
        b.localScale = Vector3.one;
        // 文字が右から入る（入るときに少し行きすぎて戻る）、しま模様は左から
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.32f)
        {
            float e = 1f - Mathf.Pow(1f - k, 3f);
            float over = Mathf.Sin(k * Mathf.PI) * 40f;
            textRoot.anchoredPosition = new Vector2(Mathf.Lerp(1400f, 0f, e) - over, 0);
            stripeTop.anchoredPosition = new Vector2(Mathf.Lerp(-1900f, 0f, e), 0);
            stripeBottom.anchoredPosition = new Vector2(Mathf.Lerp(1900f, 0f, e), 0);
            yield return null;
        }
        textRoot.anchoredPosition = Vector2.zero;
        stripeTop.anchoredPosition = stripeBottom.anchoredPosition = Vector2.zero;
        yield return new WaitForSeconds(0.85f);
        // 左へ抜ける
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.22f)
        {
            float e = k * k;
            textRoot.anchoredPosition = new Vector2(Mathf.Lerp(0f, -1500f, e), 0);
            stripeTop.anchoredPosition = new Vector2(Mathf.Lerp(0f, 1900f, e), 0);
            stripeBottom.anchoredPosition = new Vector2(Mathf.Lerp(0f, -1900f, e), 0);
            yield return null;
        }
        for (float k = 1f; k > 0f; k -= Time.deltaTime / 0.1f) { b.localScale = new Vector3(1f, k, 1f); yield return null; }
        Destroy(band.gameObject);
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
        var bt = MakeText(banner, currentRequest.IsBoss ? "WARNING　強敵戦：ウイルスの巣を検知しました" : "WARNING　ウイルスの侵入を検知しました", 52, Color.white, TextAnchor.MiddleCenter, true);
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
        StartCoroutine(TurnBanner(battle.Turn));
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
        while (rt != null && img != null)   // 絵が消えたら止める
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
        while (rt != null)
        {
            rt.anchoredPosition = new Vector2(dx, 0);
            yield return new WaitForSeconds(Random.Range(1.5f, 3.5f));
            for (int i = 0; i < 6; i++)
            {
                if (rt == null) yield break;
                rt.anchoredPosition = new Vector2(dx * Random.Range(1f, 4f), Random.Range(-6f, 6f));
                yield return new WaitForSeconds(0.04f);
            }
        }
    }

    IEnumerator SpinForever(RectTransform rt, float speed)
    {
        while (rt != null) { rt.localRotation = Quaternion.Euler(0, 0, Time.time * speed); yield return null; }
    }

    IEnumerator Float(RectTransform rt)
    {
        var img = rt.GetComponent<Image>();
        float ph = Random.value * 10f, sp = Random.Range(0.02f, 0.06f);
        while (rt != null && img != null)
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
        while (rt != null) { rt.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(Time.time * 4f)); yield return null; }
    }

    IEnumerator BlinkText(Text t)
    {
        while (t != null) { t.color = new Color(t.color.r, t.color.g, t.color.b, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f)); yield return null; }
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
        var names = new[] { "プログラム", "アイテム", "チャージ", "リンクプログラム", "ガード" };
        var acts = new UnityEngine.Events.UnityAction[] { OnProgramCommand, () => OnNotReady("アイテム"), OnChargeCommand, OnLinkCommand, OnGuardCommand };
        for (int i = 0; i < names.Length; i++)
        {
            var b = CommandButton(commandMenu, names[i], 32, acts[i], false);
            Anchor((RectTransform)b.transform, i * 0.025f, 0.67f - i * 0.11f, 1f, 0.77f - i * 0.11f);
            var label = b.GetComponentInChildren<Text>();
            label.supportRichText = true;
            if (i == 1) label.color = new Color(0.55f, 0.6f, 0.65f);   // アイテムはまだ
            if (i == 2) chargeLabel = label;
            if (i == 3) linkLabel = label;
            commandButtons.Add(b);
        }
        var go = CommandButton(commandMenu, "バトル開始", 34, OnGoButton, true);
        goLabel = go.GetComponentInChildren<Text>();
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
        var back = CommandButton(rp.rectTransform, "依頼一覧へ", 36, ShowRequests, false);
        Anchor((RectTransform)back.transform, 0.52f, 0.26f, 0.7f, 0.34f);
        resultPanel.SetActive(false);

        // リンクプログラムのペア選び
        var lp = Img(battleLayer, null, new Color(0, 0, 0, 0.55f));
        lp.raycastTarget = true;
        Fill(lp.rectTransform);
        linkPicker = lp.gameObject;
        var box = Img(lp.rectTransform, UiKit.RoundRect(24), new Color(0.08f, 0.05f, 0.16f, 0.95f));
        box.type = Image.Type.Sliced;
        Anchor(box.rectTransform, 0.32f, 0.3f, 0.68f, 0.72f);
        var lt = MakeText(box.rectTransform, "ペアになるロボットを選ぼう", 36, Pink, TextAnchor.MiddleCenter, true);
        Anchor(lt.rectTransform, 0, 0.82f, 1, 0.97f);
        var note = MakeText(box.rectTransform, "メモリが2人分になり、ペアの特性も効く。ペアの相手はこのターン魔法を打てない", 20, Color.white, TextAnchor.MiddleCenter, false);
        Anchor(note.rectTransform, 0.04f, 0.7f, 0.96f, 0.82f);
        linkPickerList = NewRect(box.rectTransform, "List");
        Anchor(linkPickerList, 0.06f, 0.2f, 0.94f, 0.68f);
        AddVertical(linkPickerList, 14);
        var cancel = CommandButton(box.rectTransform, "キャンセル", 30, () => linkPicker.SetActive(false), false);
        Anchor((RectTransform)cancel.transform, 0.3f, 0.04f, 0.7f, 0.15f);
        linkPicker.SetActive(false);
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
        var enemyPos = battle.Enemies.Count == 2
            ? new[] { new Vector2(0.68f, 0.28f), new Vector2(0.85f, 0.27f) }
            : new[] { new Vector2(0.64f, 0.3f), new Vector2(0.76f, 0.25f), new Vector2(0.89f, 0.31f) };
        // 奥のキャラから順に置く（手前のキャラが上に重なるように）
        var order = new List<(Unit u, Vector2 pos, float scale)>();
        for (int i = 0; i < battle.Allies.Count && i < allyPos.Length; i++) order.Add((battle.Allies[i], allyPos[i], AllyScale));
        for (int i = 0; i < battle.Enemies.Count && i < enemyPos.Length; i++)
            order.Add((battle.Enemies[i], enemyPos[i], battle.Enemies[i].Art == "rootkit" ? 9f : battle.Enemies[i].Art == "troy" ? 7.5f : 6f));
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
            var status = MakeText(box, "", 22, Color.white, TextAnchor.MiddleCenter, true);
            Anchor(status.rectTransform, 0, 0.38f, 1, 0.52f);
            AddOutline(status, Color.black, 2f);
            var btn = box.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            btn.onClick.AddListener(() => OnAllyClicked(u));
            allyHud[u] = (hp, fill, box, status);
        }

        // タイムラインのアイコン（味方は顔でバーの上、敵はバーの下）
        iconPartner.Clear();
        foreach (var u in battle.Allies) icons[u] = MakeIcon(UiKit.Art(u.Art + "_idle", FaceCrop), 44f, Cyan, u);
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

        v.Aura = Img(v.Root, UiKit.Glow(), ChargeColor);
        v.Aura.rectTransform.anchoredPosition = new Vector2(0, UiKit.ArtSize(idle).y * scale * 0.45f);
        v.Aura.rectTransform.sizeDelta = new Vector2(52, 60) * scale;
        v.Aura.gameObject.SetActive(false);
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
            // 弱点・耐性・特性（頭の上）
            var weak = MakeText(v.HpBox, VirusTraits(u), 20, Color.white, TextAnchor.LowerCenter, true);
            weak.supportRichText = true;
            Anchor(weak.rectTransform, -0.4f, 1, 1.4f, 1, 0, 0, 0, 56);
            AddOutline(weak, Color.black, 2f);
        }
        return v;
    }

    RectTransform MakeIcon(Sprite sprite, float y, Color frame, Unit owner = null)
    {
        var icon = Img(timelineBar, UiKit.RoundRect(34), frame);
        icon.type = Image.Type.Sliced;
        var rt = icon.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
        rt.sizeDelta = new Vector2(70, 70);
        rt.anchoredPosition = new Vector2(0, y);
        var inner = Img(rt, UiKit.RoundRect(30), new Color(0.12f, 0.08f, 0.22f));
        inner.type = Image.Type.Sliced;
        Anchor(inner.rectTransform, 0, 0, 1, 1, 4, 4, -4, -4);
        var img = Img(inner.rectTransform, sprite, Color.white);
        img.preserveAspect = true;
        img.rectTransform.name = "Face";
        Anchor(img.rectTransform, 0, 0, 1, 1, 4, 4, -4, -4);
        if (owner != null)
        {
            // リンクのときだけ出す 2 人目の顔（右半分）
            var img2 = Img(inner.rectTransform, null, Color.white);
            img2.preserveAspect = true;
            Anchor(img2.rectTransform, 0.5f, 0, 1, 1, 0, 4, -4, -4);
            img2.gameObject.SetActive(false);
            iconPartner[owner] = img2;
        }
        return rt;
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

    // ================= 依頼一覧 =================

    void BuildRequests()
    {
        requestLayer = Layer("Requests");
        var bg = requestLayer.gameObject.AddComponent<RawImage>();
        bg.texture = UiKit.DiagonalGradient(UiKit.Hex("ffc3e6"), UiKit.Hex("b4e0ff"));
        var mc = Img(requestLayer, UiKit.MagicCircle(), new Color(1, 1, 1, 0.35f)).rectTransform;
        mc.anchorMin = mc.anchorMax = new Vector2(0.12f, 0.15f);
        mc.sizeDelta = new Vector2(700, 700);
        ambient.StartCoroutine(SpinForever(mc, -5f));

        var header = Img(requestLayer, UiKit.Slant(), UiKit.Hex("1e5cf0"));
        header.type = Image.Type.Sliced;
        Anchor(header.rectTransform, -0.02f, 0.88f, 0.3f, 0.97f);
        var ht = MakeText(header.rectTransform, "依頼一覧", 56, Color.white, TextAnchor.MiddleCenter, true);
        ht.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(ht, UiKit.Hex("0a1c60"), 3f);
        var sub = MakeText(requestLayer, "どのパソコンを助けに行く？　リモートでつないで、ウイルスを撃退しよう", 30, Ink, TextAnchor.MiddleLeft, true);
        Anchor(sub.rectTransform, 0.32f, 0.88f, 0.98f, 0.97f);

        requestCards = NewRect(requestLayer, "Cards");
        Anchor(requestCards, 0.03f, 0.14f, 0.97f, 0.85f);

        var back = CommandButton(requestLayer, "ホームへ", 32, ShowHome, false);
        Anchor((RectTransform)back.transform, 0.03f, 0.03f, 0.2f, 0.11f);
        requestLayer.gameObject.SetActive(false);
    }

    Request shownRequest;   // 依頼一覧の右に出している依頼

    // 依頼一覧：左に一覧（タイトル・★・最高ランク）、右に選んだ依頼の詳しい内容
    void RebuildRequestCards()
    {
        ClearChildren(requestCards);
        if (shownRequest == null) shownRequest = System.Array.Find(Requests.All, x => !IsCleared(x)) ?? Requests.All[0];
        var listBg = Img(requestCards, UiKit.RoundRect(22), new Color(1, 1, 1, 0.55f));
        listBg.type = Image.Type.Sliced;
        Anchor(listBg.rectTransform, 0, 0, 0.42f, 1);
        var list = NewRect(listBg.rectTransform, "List");
        Anchor(list, 0, 0, 1, 1, 12, 12, -12, -12);
        AddVertical(list, 8);
        foreach (var req in Requests.All)
        {
            var r = req;
            bool sel = r == shownRequest;
            var row = Img(list, UiKit.RoundRect(14), sel ? (r.IsBoss ? Magenta : Purple) : Color.white);
            row.type = Image.Type.Sliced;
            row.raycastTarget = true;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
            var b = row.gameObject.AddComponent<Button>();
            b.targetGraphic = row;
            b.onClick.AddListener(() => { shownRequest = r; RebuildRequestCards(); });
            var tc = sel ? Color.white : Ink;
            var t1 = MakeText(row.rectTransform, r.Client + " さんから" + (r.IsBoss ? "　<color=#ffd84a>強敵戦</color>" : ""), 20, sel ? Color.white : UiKit.Hex("6a5a80"), TextAnchor.UpperLeft, true);
            t1.supportRichText = true;
            Anchor(t1.rectTransform, 0, 0, 1, 1, 18, 8, -150, -10);
            var t2 = MakeText(row.rectTransform, r.Title, 26, tc, TextAnchor.LowerLeft, true);
            Anchor(t2.rectTransform, 0, 0, 1, 1, 18, 12, -150, -8);
            var st = MakeText(row.rectTransform, new string('★', r.Stars) + new string('☆', 3 - r.Stars), 22, UiKit.Hex("f0a800"), TextAnchor.UpperRight, true);
            Anchor(st.rectTransform, 0, 0, 1, 1, 0, 8, -16, -10);
            RankBadge(row.rectTransform, BestRank(r), new Vector2(1, 0), new Vector2(-50, 30), 0.8f);
        }

        // 右：くわしい内容
        var r0 = shownRequest;
        var border = Img(requestCards, UiKit.RoundRect(28), r0.IsBoss ? Magenta : Purple);
        border.type = Image.Type.Sliced;
        Anchor(border.rectTransform, 0.44f, 0, 1, 1);
        var card = Img(border.rectTransform, UiKit.RoundRect(24), new Color(1, 1, 1, 0.96f));
        card.type = Image.Type.Sliced;
        var c = card.rectTransform;
        Anchor(c, 0, 0, 1, 1, 6, 6, -6, -6);
        var mail = Img(c, UiKit.RoundRect(8), Pink);
        mail.type = Image.Type.Sliced;
        Anchor(mail.rectTransform, 0, 1, 0, 1, 24, -84, 84, -24);
        MakeText(mail.rectTransform, "依頼", 22, Color.white, TextAnchor.MiddleCenter, true);
        var from = MakeText(c, r0.Client + " さんから　" + "<color=#f0a800>" + new string('★', r0.Stars) + new string('☆', 3 - r0.Stars) + "</color>", 28, Ink, TextAnchor.MiddleLeft, true);
        from.supportRichText = true;
        Anchor(from.rectTransform, 0, 1, 1, 1, 100, -84, -20, -24);
        var title = MakeText(c, r0.Title, 40, r0.IsBoss ? Magenta : Purple, TextAnchor.UpperLeft, true);
        Anchor(title.rectTransform, 0, 1, 1, 1, 24, -150, -24, -96);
        var msg = MakeText(c, r0.Message, 24, Ink, TextAnchor.UpperLeft, false);
        Anchor(msg.rectTransform, 0, 0.6f, 1, 1, 24, 0, -24, -156);
        var hintBg = Img(c, UiKit.RoundRect(14), UiKit.Hex("fff4d8"));
        hintBg.type = Image.Type.Sliced;
        Anchor(hintBg.rectTransform, 0, 0.43f, 1, 0.6f, 20, 0, -20, -6);
        var hint = MakeText(hintBg.rectTransform, "<color=#d08000>ヒント</color>　" + r0.Hint, 22, Ink, TextAnchor.MiddleLeft, true);
        hint.supportRichText = true;
        Anchor(hint.rectTransform, 0, 0, 1, 1, 18, 4, -18, -4);
        var vl = MakeText(c, r0.IsBoss ? "最初のウイルス（奥にまだいる…）" : "見つかったウイルス", 20, UiKit.Hex("6a5a80"), TextAnchor.LowerLeft, true);
        Anchor(vl.rectTransform, 0, 0.37f, 1, 0.42f, 24, 0, -24, 0);
        for (int k = 0; k < r0.Enemies.Length; k++)
        {
            var e = r0.Enemies[k];
            var slot = Img(c, UiKit.RoundRect(14), UiKit.Hex("2a1e3a"));
            slot.type = Image.Type.Sliced;
            Anchor(slot.rectTransform, 0.04f + k * 0.2f, 0.16f, 0.22f + k * 0.2f, 0.36f);
            var art = Img(slot.rectTransform, UiKit.Art(e.Art), Color.white);
            art.preserveAspect = true;
            Anchor(art.rectTransform, 0, 0.18f, 1, 1, 8, 4, -8, -6);
            var nm = MakeText(slot.rectTransform, e.Name, 18, Color.white, TextAnchor.LowerCenter, true);
            Anchor(nm.rectTransform, 0, 0, 1, 0.2f);
        }
        var goal = MakeText(c, "S ランクの目標：" + r0.ParTurns + " ターン以内・だれも倒れない\n最高ランク：", 22, Ink, TextAnchor.UpperLeft, true);
        Anchor(goal.rectTransform, 0.66f, 0.16f, 1, 0.36f, 0, 0, -20, 0);
        RankBadge(c, BestRank(r0), new Vector2(0.86f, 0.2f), Vector2.zero, 1.2f);
        var go = CommandButton(c, IsCleared(r0) ? "もう一度行く" : "助けに行く", 34, () => OnRequestChosen(r0), true);
        Anchor((RectTransform)go.transform, 0.4f, 0.02f, 0.97f, 0.13f);
        if (IsCleared(r0))
        {
            var stamp = Img(c, UiKit.RoundRect(12), new Color(0.18f, 0.68f, 0.29f, 0.9f));
            stamp.type = Image.Type.Sliced;
            stamp.rectTransform.anchorMin = stamp.rectTransform.anchorMax = new Vector2(0.85f, 0.88f);
            stamp.rectTransform.sizeDelta = new Vector2(170, 60);
            stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 12f);
            MakeText(stamp.rectTransform, "駆除ずみ", 30, Color.white, TextAnchor.MiddleCenter, true);
        }
    }

    // ---- ランク（S・A・B） ----

    static int BestRank(Request r) => PlayerPrefs.GetInt("rank_" + r.Id, 0);   // 0 = まだ、1 = B、2 = A、3 = S
    static string RankLetter(int rank) => rank >= 3 ? "S" : rank == 2 ? "A" : rank == 1 ? "B" : "";
    static Color RankColor(int rank) => rank >= 3 ? UiKit.Hex("ffb000") : rank == 2 ? UiKit.Hex("e0408a") : UiKit.Hex("4a8fe0");

    // ランクの丸いバッジ（まだならなにも出さない）
    void RankBadge(RectTransform parent, int rank, Vector2 anchor, Vector2 pos, float scale)
    {
        if (rank <= 0) return;
        var b = Img(parent, UiKit.Circle(), RankColor(rank)).rectTransform;
        b.anchorMin = b.anchorMax = anchor;
        b.anchoredPosition = pos;
        b.sizeDelta = new Vector2(64, 64) * scale;
        var ol = b.gameObject.AddComponent<Outline>();
        ol.effectColor = Color.white;
        ol.effectDistance = new Vector2(3, -3);
        var t = MakeText(b, RankLetter(rank), (int)(40 * scale), Color.white, TextAnchor.MiddleCenter, true);
        t.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(t, new Color(0, 0, 0, 0.5f), 2f);
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
        headerImg = Img(screen, UiKit.Slant(), UiKit.Hex("1e5cf0"));
        headerImg.type = Image.Type.Sliced;
        Anchor(headerImg.rectTransform, -0.02f, 0.9f, 0.28f, 0.98f);
        headerText = MakeText(headerImg.rectTransform, "プログラム", 52, Color.white, TextAnchor.MiddleCenter, true);
        headerText.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(headerText, UiKit.Hex("0a1c60"), 3f);
        tabsRoot = NewRect(screen, "Tabs");
        Fill(tabsRoot);

        // ロボットを切りかえるタブ（顔と名前）
        robotTabs.Clear();
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var tab = Img(tabsRoot, UiKit.RoundRect(18), Color.white);
            tab.type = Image.Type.Sliced;
            tab.raycastTarget = true;
            Anchor(tab.rectTransform, 0.3f + i * 0.13f, 0.9f, 0.42f + i * 0.13f, 0.98f);
            var b = tab.gameObject.AddComponent<Button>();
            b.targetGraphic = tab;
            b.onClick.AddListener(() =>
            {
                if (idx >= battle.Allies.Count) return;
                var u = battle.Allies[idx];
                if (!u.Alive) return;
                if (u.LinkedTo != null) { programMessage.text = u.Name + "は" + u.LinkedTo.Name + "とリンク中です"; return; }
                if (decided.Contains(u)) { programMessage.text = u.Name + "の行動は決定ずみです（選び直せません）"; return; }
                OpenProgram(u, u.LinkPartner);
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
        paletteList = ScrollArea(palette.rectTransform, 16, 16, -16, -70, 10);
        RebuildPalette();

        // コード
        var codeBg = Img(win.rectTransform, UiKit.RoundRect(18), UiKit.Hex("efc4e6"));
        codeBg.type = Image.Type.Sliced;
        Anchor(codeBg.rectTransform, 0.39f, 0.03f, 0.98f, 0.97f);
        codeArea = codeBg.rectTransform;
        var cTitle = MakeText(codeArea, "コード", 30, Ink, TextAnchor.UpperLeft, false);
        Anchor(cTitle.rectTransform, 0, 1, 1, 1, 18, -56, 0, -12);
        codeList = ScrollArea(codeArea, 34, 16, -34, -64, -UiKit.BlockTabHeight + 2f);   // 下の出っぱりを次のブロックのへこみに重ねる
        MakeCapBlock(codeList, "スタート", "Start", true);
        MakeCapBlock(codeList, "フィニッシュ", "Finish", false);

        // 右：ロボット・メモリ・処理速度・送信
        var side = Img(screen, UiKit.RoundRect(28), new Color(1, 1, 1, 0.9f));
        side.type = Image.Type.Sliced;
        var sideRt = side.rectTransform;
        Anchor(sideRt, 0.68f, 0.03f, 0.985f, 0.87f);
        partnerPortrait = Img(sideRt, null, new Color(0.95f, 0.95f, 1f));
        partnerPortrait.preserveAspect = true;
        Anchor(partnerPortrait.rectTransform, 0.2f, 0.55f, 0.52f, 0.86f);
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
        var cl = CommandButton(sideRt, "リンクをやめる", 22, OnCancelLink, false);
        Anchor((RectTransform)cl.transform, 0.66f, 0.13f, 0.97f, 0.19f);
        cancelLinkButton = cl.gameObject;

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

    const float Indent = 28f;     // C の字の左の柱の太さ（中のブロックはこの分だけ右にずれる）
    const float ArmWidth = 230f;  // C の字の「でなければ」「おわり」の腕の長さ
    const float MouthHeight = 26f; // 中が空のときの口の高さ

    static Color BlockColor(BlockKind k) =>
        k == BlockKind.If || k == BlockKind.Else || k == BlockKind.EndIf ? UiKit.Hex("ffab19")
        : k == BlockKind.Repeat || k == BlockKind.EndRepeat ? UiKit.Hex("f0801e")
        : BlockColors[(int)k];

    // 中身を上下に動かせる（マウスホイール）入れ物を作り、ブロックを並べる中身を返す
    RectTransform ScrollArea(RectTransform parent, float l, float b, float r, float t, float spacing)
    {
        var view = NewRect(parent, "Viewport");
        Anchor(view, 0, 0, 1, 1, l, b, r, t);
        view.gameObject.AddComponent<RectMask2D>();
        var hit = view.gameObject.AddComponent<Image>();   // 空いている所でもホイールが効くように
        hit.color = new Color(0, 0, 0, 0);
        var content = NewRect(view, "Content");
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
        v.padding = new RectOffset(0, 0, 10, 10);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var sr = view.gameObject.AddComponent<ScrollRect>();
        sr.content = content;
        sr.viewport = view;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;
        sr.inertia = false;
        return content;
    }

    // コードを 1 行ずつ並べる。「もし」「繰り返す」は Scratch と同じ C の字：
    //   上の腕（もし ○○ なら）・左の柱・下の腕（おわり）がつながって、中のブロックを囲む
    void BuildCodeRows(List<Block> code)
    {
        var open = new List<Color>();   // いま中にいる制御ブロックの色
        for (int idx = 0; idx < code.Count; idx++)
        {
            var b = code[idx];
            bool arm = b.Kind == BlockKind.Else || b.Kind == BlockKind.EndIf || b.Kind == BlockKind.EndRepeat;
            int depth = arm ? open.Count - 1 : open.Count;
            if (arm)
            {
                var col = open.Count > 0 ? open[open.Count - 1] : BlockColor(b.Kind);
                bool isElse = b.Kind == BlockKind.Else;
                var row = CodeRow(open, depth, isElse ? 46f : 38f);
                var shape = Img(row, UiKit.BlockShape(), col);
                shape.type = Image.Type.Sliced;
                Anchor(shape.rectTransform, 0, 0, 0, 1, depth * Indent, 0, depth * Indent + ArmWidth, 0);
                var cornerUp = Img(row, null, col).rectTransform;   // 柱とつなぐ
                Anchor(cornerUp, 0, 1, 0, 1, depth * Indent, -10, depth * Indent + Indent, 10);
                var view = shape.gameObject.AddComponent<BlockView>();
                view.Init(this, b, false, canvas);
                view.Draggable = false;
                if (isElse)
                {
                    var t = MakeText(shape.rectTransform, "でなければ", 24, Color.white, TextAnchor.MiddleLeft, true);
                    Anchor(t.rectTransform, 0, 0, 1, 1, 20, UiKit.BlockTabHeight, 0, 0);
                }
                if (b.Kind == BlockKind.EndRepeat)
                {
                    var up = MakeText(shape.rectTransform, "▲", 20, Color.white, TextAnchor.MiddleRight, true);
                    Anchor(up.rectTransform, 0, 0, 1, 1, 0, UiKit.BlockTabHeight, -16, 0);
                }
                if (!isElse) open.RemoveAt(open.Count - 1);
                // 「でなければ」の下が空なら口を開けておく
                if (isElse && idx + 1 < code.Count && code[idx + 1].Kind == BlockKind.EndIf) CodeRow(open, open.Count, MouthHeight);
                continue;
            }
            var r = CodeRow(open, depth, 66f);
            var bv = MakeBlock(r, b, false);
            Anchor((RectTransform)bv.transform, 0, 0, 1, 1, depth * Indent, 0, 0, 0);
            if (b.Kind == BlockKind.If || b.Kind == BlockKind.Repeat)
            {
                open.Add(BlockColor(b.Kind));
                // 中が空なら口を開けておく（ここにドロップすると中に入る）
                var next = idx + 1 < code.Count ? code[idx + 1].Kind : BlockKind.Element;
                if (next == BlockKind.Else || next == BlockKind.EndIf || next == BlockKind.EndRepeat) CodeRow(open, open.Count, MouthHeight);
            }
        }
    }

    // コードの 1 行の入れ物。外側の C の字の柱（左の帯）を、上下の行とつながるように少しはみ出して描く
    RectTransform CodeRow(List<Color> open, int depth, float height)
    {
        var row = NewRect(codeList, "Row");
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        for (int d = 0; d < depth && d < open.Count; d++)
        {
            var bar = Img(row, null, open[d]).rectTransform;
            Anchor(bar, 0, 0, 0, 1, d * Indent, -10, d * Indent + Indent, 10);
        }
        return row;
    }

    BlockView MakeBlock(RectTransform parent, Block data, bool fromPalette)
    {
        var col = BlockColor(data.Kind);
        if (fromPalette && (data.Kind == BlockKind.If || data.Kind == BlockKind.Repeat)) return MakePaletteC(parent, data, col);
        var img = Img(parent, UiKit.BlockShape(), col);
        img.type = Image.Type.Sliced;
        var rect = img.rectTransform;
        rect.name = "Block";
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 66;
        var view = rect.gameObject.AddComponent<BlockView>();
        view.Init(this, data, fromPalette, canvas);
        BlockFace(rect, data, view, col);
        return view;
    }

    // 手持ちの「もし」「繰り返す」は C の字のまま見せる（企画書の形）
    BlockView MakePaletteC(RectTransform parent, Block data, Color col)
    {
        bool withElse = data.Kind == BlockKind.If && data.WithElse;
        float top = 66f, mouth = 22f, elseArm = 42f, bottom = 36f;
        float h = top + mouth + bottom + (withElse ? elseArm + mouth : 0f) - UiKit.BlockTabHeight;
        var root = Img(parent, null, new Color(1, 1, 1, 0));   // つかむための透明な入れ物
        root.rectTransform.name = "Block";
        root.gameObject.AddComponent<LayoutElement>().preferredHeight = h;
        var view = root.gameObject.AddComponent<BlockView>();
        view.Init(this, data, true, canvas);
        var head = Img(root.rectTransform, UiKit.BlockShape(), col);
        head.type = Image.Type.Sliced;
        Anchor(head.rectTransform, 0, 1, 1, 1, 0, -top, 0, 0);
        BlockFace(head.rectTransform, data, view, col);
        float y = -top + UiKit.BlockTabHeight;
        y = PaletteSpine(root.rectTransform, col, y, mouth);
        if (withElse)
        {
            y = PaletteArm(root.rectTransform, col, y, elseArm, "でなければ", false);
            y = PaletteSpine(root.rectTransform, col, y, mouth);
        }
        PaletteArm(root.rectTransform, col, y, bottom, "", data.Kind == BlockKind.Repeat);
        return view;
    }

    // C の字の左の柱（y から下へ len）。次の y を返す
    float PaletteSpine(RectTransform root, Color col, float y, float len)
    {
        var sp = Img(root, null, col).rectTransform;
        Anchor(sp, 0, 1, 0, 1, 0, y - len - 6, Indent, y + 6);
        return y - len;
    }

    // C の字の腕（「でなければ」や下の「おわり」）。次の y を返す
    float PaletteArm(RectTransform root, Color col, float y, float armH, string label, bool arrow)
    {
        var a = Img(root, UiKit.BlockShape(), col);
        a.type = Image.Type.Sliced;
        Anchor(a.rectTransform, 0, 1, 0, 1, 0, y - armH, ArmWidth, y);
        if (label != "")
        {
            var t = MakeText(a.rectTransform, label, 22, Color.white, TextAnchor.MiddleLeft, true);
            Anchor(t.rectTransform, 0, 0, 1, 1, 18, UiKit.BlockTabHeight, 0, 0);
        }
        if (arrow)
        {
            var up = MakeText(a.rectTransform, "▲", 18, Color.white, TextAnchor.MiddleRight, true);
            Anchor(up.rectTransform, 0, 0, 1, 1, 0, UiKit.BlockTabHeight, -14, 0);
        }
        return y - armH + UiKit.BlockTabHeight;
    }

    // ブロックの表の文字（▼ の付いた中身と、右側の言葉）
    void BlockFace(RectTransform rect, Block data, BlockView view, Color col)
    {
        // ▼ の付いた中身（クリックで変わる）
        var pill = Img(rect, UiKit.RoundRect(18), Color.Lerp(col, Color.black, 0.28f));
        pill.type = Image.Type.Sliced;
        pill.raycastTarget = true;
        Anchor(pill.rectTransform, 0, 0, 0, 1, 16, UiKit.BlockTabHeight + 8, 150, -8);
        var b = pill.gameObject.AddComponent<Button>();
        b.targetGraphic = pill;
        b.onClick.AddListener(() => OnBlockValueClicked(view));
        bool isIf = data.Kind == BlockKind.If;
        float left = isIf ? 66f : 16f, width = isIf ? 220f : 134f;   // 「もし」は条件が長いので広く
        Anchor(pill.rectTransform, 0, 0, 0, 1, left, UiKit.BlockTabHeight + 8, left + width, -8);
        var valueText = MakeText(pill.rectTransform, "", isIf ? 20 : 28, Color.white, TextAnchor.MiddleCenter, true);
        Anchor(valueText.rectTransform, 0, 0, 1, 1, 6, 0, -6, 0);
        if (isIf)
        {
            var moshi = MakeText(rect, "もし", 24, Color.white, TextAnchor.MiddleLeft, true);
            Anchor(moshi.rectTransform, 0, 0, 1, 1, 16, UiKit.BlockTabHeight, 0, 0);
        }
        var suffix = MakeText(rect, "", 24, Color.white, TextAnchor.MiddleLeft, true);
        Anchor(suffix.rectTransform, 0, 0, 1, 1, left + width + 10, UiKit.BlockTabHeight, 0, 0);
        suffix.horizontalOverflow = HorizontalWrapMode.Overflow;   // 折り返さない（「なら」が縦にならないように）
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
    public bool Draggable = true;   // 「でなければ」「おわり」は単独では動かせない
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
        string mem = Data.Kind == BlockKind.MagicLink ? "メモリ全部" : Blocks.Memory(Data.Kind, Data.Value).ToString();
        suffixText.text = Blocks.Suffix(Data.Kind) + "  <size=18>(" + mem + ")</size>";
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!Draggable) return;
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
        if (!Draggable) return;
        if (ghost != null) Destroy(ghost.gameObject);
        ghost = null;
        var mine = GetComponent<CanvasGroup>();
        if (mine != null) mine.alpha = 1f;
        game.OnBlockDropped(this, e.position);
    }
}
