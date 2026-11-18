// 魔法プログラムバトルの画面と操作。シーンを再生すると自動で起動する（シーンに何も置かなくてよい）
//   バトル画面:     「プログラムを組む」→ ブロックを並べて送信 →「バトル開始」でタイムラインが進む
//                   敵をクリックすると「1体」の魔法でねらう敵を選べる
//   プログラム画面: 左の手持ちブロックをドラッグして中央のコードに置く。コードの外へドラッグすると消える
//                   ブロックの白い枠をクリックすると中身（炎・氷…、数）が変わる
// 世界観: プレイヤーはプログラマー。パソコンの中でプログラムを組んで魔法を作り、AI ロボットに送ってウイルスを撃退する
// キャラクターはドット絵（PixelArt.cs）。背景は電子回路の方眼をコードで描いている
// アニメーション: 待機中のゆれ・まばたき、攻撃の踏み込み、魔法の弾、当たったときのゆれと火花、ウイルスが消える演出
// ノイズ: ウイルスが侵入してくる登場演出（警告・砂嵐・色ずれ）、ウイルスの攻撃や待機中のちらつき

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using MagicBattle;

public class MagicGame : MonoBehaviour
{
    static readonly Color Background = new Color(0.04f, 0.06f, 0.12f);
    static readonly Color PanelColor = new Color(0.20f, 0.20f, 0.30f, 0.95f);
    static readonly Color ButtonColor = new Color(0.95f, 0.95f, 1f);
    static readonly Color EnemyColor = new Color(0.35f, 0.12f, 0.22f);
    static readonly Color TargetColor = new Color(1f, 0.85f, 0.2f);
    static readonly Color[] AllyColors = { new Color(0.55f, 0.20f, 0.32f), new Color(0.16f, 0.30f, 0.58f), new Color(0.55f, 0.40f, 0.10f) };
    // ブロックの色（属性=紫, 対象=青, 威力=緑, 発射方法=ピンク, 追加効果=黄緑）
    static readonly Color[] BlockColors =
    {
        new Color(0.50f, 0.35f, 0.75f), new Color(0.30f, 0.40f, 0.80f), new Color(0.25f, 0.65f, 0.35f),
        new Color(0.85f, 0.30f, 0.60f), new Color(0.60f, 0.62f, 0.20f),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<MagicGame>() == null) new GameObject("MagicGame").AddComponent<MagicGame>();
    }

    enum Phase { Planning, Running, Finished }

    Battle battle;
    Phase phase;
    readonly Dictionary<Unit, List<Block>> drafts = new Dictionary<Unit, List<Block>>(); // 組みかけのコード
    Unit editing;          // プログラム画面で組んでいるロボット
    bool busy;             // 行動のアニメーション中はタイムラインを止める
    int flying;            // 飛んでいる魔法の弾の数
    const float TimelineSpeed = 1.0f;

    Font font;
    Canvas canvas;
    RectTransform root;
    RectTransform cardLayer; // キャラクターのカードを置く場所（プログラム画面より下）
    RectTransform effects;  // 魔法の弾・火花を置く場所（バトルの上、プログラム画面の下）
    RectTransform noiseLayer;  // 画面全体のノイズ（砂嵐・横すじ・警告）
    RawImage noise;
    bool intro;             // ウイルスの登場演出中（ボタンを押せない）

    // バトル画面
    Text turnText, messageText, logText;
    readonly Dictionary<Unit, UnitCard> cards = new Dictionary<Unit, UnitCard>();
    readonly Dictionary<Unit, RectTransform> icons = new Dictionary<Unit, RectTransform>();
    RectTransform timelineBar;
    Button programButton, startButton, retryButton;
    readonly List<string> log = new List<string>();

    // プログラム画面
    GameObject programScreen;
    RectTransform codeArea, codeList, paletteList;
    Text memoryText, speedText, previewText, programMessage, programTitle;
    Image memoryFill;
    readonly List<(Unit unit, Image bg, Text text)> robotTabs = new List<(Unit, Image, Text)>();

    class UnitCard
    {
        public RectTransform Rect;
        public Outline Frame;   // ふちどり（ねらう敵は黄色、行動中は白）
        public Image HpFill;
        public Text HpText, InfoText;
        public RectTransform Art;
        public Image ArtImage;
        public Sprite Normal, Blink;  // ふだんの絵と、目を閉じた絵（味方だけ）
        public Vector2 Offset;        // 踏み込み・ゆれで動かす量
        public float Phase;           // 待機のゆれをキャラごとにずらす
        public float BlinkTimer;
        public int ShownHp;           // 画面に出している HP（弾が当たったときに減らす）
        public bool Dying, Dead;
        public float GlitchTimer;     // ウイルスがときどきちらつくまでの時間
    }

    void Start()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;

        // UI の文字のフォント。Assets/Resources/UIFont/ に日本語フォントを置けばそれを使う。無ければ PC のフォント
        var uiFonts = Resources.LoadAll<Font>("UIFont");
        font = uiFonts.Length > 0 ? uiFonts[0]
             : Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic" }, 64);

        BuildUI();
        NewBattle();
    }

    void NewBattle()
    {
        StopAllCoroutines();
        for (int i = effects.childCount - 1; i >= 0; i--) Destroy(effects.GetChild(i).gameObject);
        for (int i = noiseLayer.childCount - 1; i >= 2; i--) Destroy(noiseLayer.GetChild(i).gameObject); // 走査線と砂嵐は残す
        busy = false;
        flying = 0;
        battle = new Battle(System.Environment.TickCount);
        drafts.Clear();
        foreach (var a in battle.Allies) drafts[a] = new List<Block>();
        editing = battle.Allies[0];
        for (int i = 0; i < robotTabs.Count; i++) robotTabs[i] = (battle.Allies[i], robotTabs[i].bg, robotTabs[i].text);
        phase = Phase.Planning;
        log.Clear();
        AddLog("パソコンにウイルスが侵入！ 「プログラムを組む」で魔法を作って撃退しよう");
        BuildUnitViews();
        programScreen.SetActive(false);
        noise.color = new Color(1, 1, 1, 0);
        RefreshBattle();
        StartCoroutine(Intro());
    }

    // ================= 毎フレーム =================

    void Update()
    {
        if (phase == Phase.Running)
        {
            if (!busy) StepBattle(Time.deltaTime);
        }
        UpdateTimeline();
        AnimateCards();
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
        if (battle == null || timelineBar == null) return;
        float w = timelineBar.rect.width;
        foreach (var kv in icons)
        {
            var u = kv.Key;
            kv.Value.gameObject.SetActive(cards.TryGetValue(u, out var cd) ? !cd.Dead : u.Alive);
            float t = Mathf.Clamp01(u.Gauge / Battle.GaugeLength);
            var p = kv.Value.anchoredPosition;
            p.x = t * w;
            kv.Value.anchoredPosition = p;
        }
    }

    // ================= ボタン =================

    void OnProgramButton()
    {
        if (phase != Phase.Planning) return;
        if (!editing.Alive) editing = battle.Allies.Find(a => a.Alive) ?? editing;
        programScreen.SetActive(true);
        RefreshProgram();
    }

    void OnStartButton()
    {
        if (phase != Phase.Planning) return;
        programScreen.SetActive(false);
        phase = Phase.Running;
        var none = battle.Allies.FindAll(a => a.Alive && a.Spell == null);
        if (none.Count > 0) AddLog("プログラムを送信していないロボットは何もしません");
        RefreshBattle();
    }

    void OnEnemyClicked(Unit e)
    {
        if (!e.Alive) return;
        battle.TargetIndex = battle.Enemies.IndexOf(e);
        RefreshBattle();
    }

    void OnSend()
    {
        var code = drafts[editing];
        if (!Compiler.Check(code, editing.Capacity, out string msg))
        {
            programMessage.text = "送信できません：" + msg;
            programMessage.color = new Color(1f, 0.5f, 0.5f);
            return;
        }
        var spell = Compiler.Build(code);
        battle.SetSpell(editing, spell);
        programMessage.text = "送信しました！ " + editing.Name + "：" + spell.Describe();
        programMessage.color = new Color(0.6f, 1f, 0.6f);
        AddLog(editing.Name + "にプログラムを送信（" + spell.Describe() + "）");
        RefreshBattle();
        RefreshProgram(keepMessage: true);
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
            // 置いた高さより下にある最初のブロックの前に入れる
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
        turnText.text = "ターン " + battle.Turn;
        messageText.text = phase == Phase.Planning ? "プログラムを組んで「バトル開始」"
                         : phase == Phase.Running ? "バトル中…"
                         : battle.Won ? "ウイルスを全部駆除した！" : "システムダウン…";
        var target = battle.CurrentTarget();
        foreach (var kv in cards)
        {
            var u = kv.Key;
            var c = kv.Value;
            c.HpFill.fillAmount = (float)c.ShownHp / u.MaxHp;
            c.HpText.text = "HP " + c.ShownHp + " / " + u.MaxHp;
            if (u.IsAlly)
            {
                c.InfoText.text = u.TraitName() + "\n"
                    + (u.Spell == null ? "プログラム：なし" : u.Spell.Describe() + "\n処理速度：" + Compiler.SpeedLabel(u.Spell.Memory, u.Capacity));
            }
            else
            {
                c.InfoText.text = (u.HasWeakness ? "弱点：" + Blocks.Label(BlockKind.Element, (int)u.Weakness) : "弱点：なし")
                                + "\n攻撃力：" + u.Attack + (u == target ? "\n◆ ねらう敵" : "");
                c.Frame.effectColor = u == target ? TargetColor : new Color(0, 0, 0, 0);
            }
            c.Rect.GetComponent<CanvasGroup>().alpha = c.ShownHp > 0 ? 1f : 0.35f;
        }
        programButton.interactable = phase == Phase.Planning && !intro;
        startButton.interactable = phase == Phase.Planning && !intro;
        retryButton.gameObject.SetActive(phase == Phase.Finished);
    }

    void RefreshProgram(bool keepMessage = false)
    {
        var code = drafts[editing];
        foreach (var t in robotTabs)
        {
            bool sel = t.unit == editing;
            t.bg.color = sel ? TargetColor : ButtonColor;
            t.text.text = t.unit.Name + (t.unit.Spell != null ? "（送信済み）" : "") + (t.unit.Alive ? "" : "（戦闘不能）");
        }
        programTitle.text = editing.Name + "のプログラム　（特性：" + editing.TraitName() + "）";

        // コードを並べ直す
        for (int i = codeList.childCount - 1; i >= 0; i--)
        {
            var ch = codeList.GetChild(i);
            if (ch.GetComponent<BlockView>() != null) Destroy(ch.gameObject);
        }
        var finish = codeList.Find("Finish");
        foreach (var b in code) MakeBlock(codeList, b, false);
        if (finish != null) finish.SetAsLastSibling();

        int used = Compiler.MemoryUsed(code);
        memoryText.text = "メモリ　" + used + " / " + editing.Capacity;
        memoryFill.fillAmount = Mathf.Clamp01((float)used / editing.Capacity);
        memoryFill.color = used > editing.Capacity ? new Color(1f, 0.35f, 0.35f) : new Color(0.4f, 0.85f, 1f);
        speedText.text = "処理速度　" + Compiler.SpeedLabel(used, editing.Capacity);

        bool ok = Compiler.Check(code, editing.Capacity, out string msg);
        if (ok)
        {
            var s = Compiler.Build(code);
            var t = battle.CurrentTarget();
            int dmg = Battle.Damage(editing, s, t);
            previewText.text = "見込み：1発 " + dmg + " ダメージ（" + (t != null ? t.Name : "-") + "に）× " + s.Shots + "回"
                             + (s.Target == TargetType.All ? "、敵全員に" : "");
        }
        else previewText.text = msg;

        if (!keepMessage) { programMessage.text = ""; }
    }

    void AddLog(string s)
    {
        log.Add(s);
        if (log.Count > 6) log.RemoveAt(0);
        logText.text = string.Join("\n", log);
    }

    // ================= 演出 =================

    IEnumerator Flash(Outline frame)
    {
        frame.effectColor = Color.white;
        yield return new WaitForSeconds(0.3f);
        if (frame != null) frame.effectColor = new Color(0, 0, 0, 0);
        RefreshBattle();
    }

    // 待機中のゆれ・まばたき
    void AnimateCards()
    {
        float t = Time.time;
        foreach (var kv in cards)
        {
            var u = kv.Key;
            var c = kv.Value;
            if (c.Dead || c.Dying) continue;
            float bob = u.IsAlly ? Mathf.Sin(t * 2.2f + c.Phase) * 4f : Mathf.Sin(t * 3.0f + c.Phase) * 3f;
            c.Art.anchoredPosition = new Vector2(0, bob) + c.Offset;
            if (!u.IsAlly)
            {
                float k = 1f + Mathf.Sin(t * 2.5f + c.Phase) * 0.05f; // ウイルスはぷよぷよ
                c.Art.localScale = new Vector3(k, 2f - k, 1f);
                c.GlitchTimer -= Time.deltaTime;                       // ときどきちらつく
                if (c.GlitchTimer <= 0f && !intro)
                {
                    c.GlitchTimer = Random.Range(2.5f, 6f);
                    StartCoroutine(GlitchArt(c, 0.15f));
                }
            }
            else if (c.Blink != null)
            {
                c.BlinkTimer -= Time.deltaTime;
                if (c.BlinkTimer < -0.12f) { c.BlinkTimer = Random.Range(2f, 5f); c.ArtImage.sprite = c.Normal; }
                else if (c.BlinkTimer < 0f) c.ArtImage.sprite = c.Blink;
            }
        }
    }

    static Color ElementColor(Element e) =>
        e == Element.Fire ? new Color(1f, 0.5f, 0.15f) : e == Element.Ice ? new Color(0.45f, 0.9f, 1f) : new Color(0.9f, 0.85f, 1f);

    // 1 回の行動のアニメーション。終わるまでタイムラインを止める
    IEnumerator PlayAction(ActionResult r)
    {
        busy = true;
        var actor = cards[r.Actor];
        StartCoroutine(Flash(actor.Frame));
        RectTransform icon;
        if (icons.TryGetValue(r.Actor, out icon)) StartCoroutine(Bump(icon));
        yield return Lunge(actor, r.Actor.IsAlly ? 1f : -1f);

        if (r.Actor.IsAlly && r.Actor.Spell != null)
        {
            var s = r.Actor.Spell;
            var color = ElementColor(s.Element);
            bool blast = s.HasEffect && s.Effect == Effect.Blast;
            foreach (var h in r.Hits)
            {
                if (h.Heal) continue;
                StartCoroutine(Projectile(actor, h, color, blast, s.HasEffect && s.Effect == Effect.Slow));
                yield return new WaitForSeconds(0.12f);
            }
            while (flying > 0) yield return null;
            foreach (var h in r.Hits)
                if (h.Heal) Impact(h, new Color(0.5f, 1f, 0.5f), false);
        }
        else
        {
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
        }
        RefreshBattle();
    }

    // 相手の方へ踏み込んで戻る
    IEnumerator Lunge(UnitCard c, float dir)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.12f) { c.Offset = new Vector2(dir * 45f * t, 6f * t); yield return null; }
        for (float t = 1f; t > 0f; t -= Time.deltaTime / 0.18f) { c.Offset = new Vector2(dir * 45f * t, 6f * t); yield return null; }
        c.Offset = Vector2.zero;
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
    IEnumerator Projectile(UnitCard from, Hit h, Color color, bool big, bool slow)
    {
        flying++;
        var orb = Box(effects, color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        orb.sizeDelta = Vector2.one * (big ? 40f : 28f);
        orb.GetComponent<Image>().raycastTarget = false;
        var core = Box(orb, Color.white, new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.7f));
        core.GetComponent<Image>().raycastTarget = false;
        Vector3 a = from.Art.position;
        UnitCard tc;
        Vector3 b = cards.TryGetValue(h.Target, out tc) ? tc.Art.position : a;
        float trailTimer = 0f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            var p = Vector3.Lerp(a, b, t);
            p.y += Mathf.Sin(t * Mathf.PI) * 80f;
            orb.position = p;
            orb.localRotation = Quaternion.Euler(0, 0, t * 360f);
            trailTimer -= Time.deltaTime;
            if (trailTimer <= 0f) { trailTimer = 0.03f; Burst(p, color, 1, 10f, 40f); } // 尾
            yield return null;
        }
        Destroy(orb.gameObject);
        Impact(h, color, big);
        if (slow && tc != null) Burst(b, new Color(0.6f, 0.6f, 1f), 6, 12f, 120f);
        flying--;
    }

    // 当たった（回復した）ときの演出と、画面の HP の更新
    void Impact(Hit h, Color color, bool big)
    {
        if (!cards.TryGetValue(h.Target, out var c)) return;
        c.ShownHp = Mathf.Clamp(c.ShownHp + (h.Heal ? h.Amount : -h.Amount), 0, h.Target.MaxHp);
        ShowPopup(h);
        Burst(c.Art.position, color, big ? 18 : 9, big ? 16f : 12f, big ? 420f : 260f);
        if (!h.Heal)
        {
            StartCoroutine(Shake(c));
            StartCoroutine(Tint(c.ArtImage, new Color(1f, 0.45f, 0.45f)));
        }
        else StartCoroutine(Tint(c.ArtImage, new Color(0.6f, 1f, 0.6f)));
        if (c.ShownHp <= 0 && !c.Dying && !c.Dead) StartCoroutine(Die(c));
        RefreshBattle();
    }

    IEnumerator Shake(UnitCard c)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            c.Offset = Random.insideUnitCircle * 10f * (1f - t);
            yield return null;
        }
        c.Offset = Vector2.zero;
    }

    IEnumerator Tint(Image img, Color color)
    {
        img.color = color;
        yield return new WaitForSeconds(0.15f);
        if (img != null) img.color = Color.white;
    }

    // 倒れる演出：ウイルスはノイズのようにちらついて消える。ロボットは倒れて暗くなる
    IEnumerator Die(UnitCard c)
    {
        c.Dying = true;
        yield return new WaitForSeconds(0.2f);
        for (int i = 0; i < 10; i++)
        {
            if (c.Art == null) yield break;
            c.ArtImage.enabled = i % 2 == 0;
            c.Art.anchoredPosition = new Vector2(Random.Range(-12f, 12f), 0);
            c.Art.localScale = new Vector3(1f + Random.Range(-0.2f, 0.3f), 1f - i * 0.06f, 1f);
            yield return new WaitForSeconds(0.05f);
        }
        if (c.Art == null) yield break;
        c.ArtImage.enabled = true;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f)
        {
            c.Art.localScale = new Vector3(1f + t, Mathf.Max(0.02f, 0.4f * (1f - t)), 1f);
            yield return null;
        }
        c.ArtImage.enabled = false;
        c.Dying = false;
        c.Dead = true;
    }

    // ================= ノイズ（ウイルスの侵入） =================

    // 登場演出：警告 → 砂嵐 → ウイルスが 1 体ずつ色ずれしながら現れる
    IEnumerator Intro()
    {
        intro = true;
        RefreshBattle();
        foreach (var e in battle.Enemies) { var c = cards[e]; c.ArtImage.enabled = false; c.Dying = true; }

        yield return new WaitForSeconds(0.3f);
        var banner = Box(noiseLayer, new Color(0.75f, 0.05f, 0.15f, 0.9f), new Vector2(0, 0.52f), new Vector2(1, 0.64f));
        banner.GetComponent<Image>().raycastTarget = false;
        var bt = MakeText(banner, "WARNING　ウイルスの侵入を検知しました", 48, TextAnchor.MiddleCenter);
        bt.fontStyle = FontStyle.Bold;
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
            yield return Materialize(cards[e]);
        }
        intro = false;
        RefreshBattle();
    }

    // ノイズの中からちらつきながら現れる
    IEnumerator Materialize(UnitCard c)
    {
        c.ArtImage.enabled = true;
        StartCoroutine(GlitchArt(c, 0.45f));
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
        {
            if (c.Art == null) yield break;
            c.ArtImage.enabled = Random.value < 0.3f + t * 0.7f;
            float sy = Mathf.Min(1.15f, t * 1.4f);
            c.Art.localScale = new Vector3(1f + (1f - t) * Random.Range(0f, 0.6f), Mathf.Max(0.05f, sy), 1f);
            c.Art.anchoredPosition = new Vector2(Random.Range(-20f, 20f) * (1f - t), 0);
            yield return null;
        }
        c.ArtImage.enabled = true;
        c.Art.localScale = Vector3.one;
        c.Dying = false;
    }

    // 絵の色ずれ（水色と赤紫の写しが横にずれて、ずれが戻る）
    IEnumerator GlitchArt(UnitCard c, float dur)
    {
        if (c.Art == null || c.Dead) yield break;
        var ghosts = new List<Image>();
        foreach (var col in new[] { new Color(0f, 1f, 1f, 0.6f), new Color(1f, 0f, 0.8f, 0.6f) })
        {
            var go = new GameObject("Glitch", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(c.Art, false);
            Place(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var img = go.GetComponent<Image>();
            img.sprite = c.ArtImage.sprite;
            img.preserveAspect = true;
            img.color = col;
            img.raycastTarget = false;
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
            noise.uvRect = new Rect(Random.value, Random.value, 1920f / 4f / 160f, 1080f / 4f / 90f);
            if (Random.value < k) StartCoroutine(Streak(k));
            yield return null;
        }
        noise.color = new Color(1, 1, 1, 0);
    }

    // 一瞬だけ出る横すじ
    IEnumerator Streak(float k)
    {
        var colors = new[] { new Color(0f, 1f, 1f), new Color(1f, 0f, 0.8f), Color.white, new Color(0.3f, 1f, 0.4f) };
        var c = colors[Random.Range(0, colors.Length)];
        c.a = Random.Range(0.2f, 0.6f);
        float y = Random.value;
        float h = Random.Range(0.004f, 0.03f + 0.04f * k);
        float x0 = Random.Range(-0.2f, 0.4f);
        var bar = Box(noiseLayer, c, new Vector2(x0, y), new Vector2(x0 + Random.Range(0.3f, 1f), Mathf.Min(1f, y + h)));
        bar.GetComponent<Image>().raycastTarget = false;
        yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
        Destroy(bar.gameObject);
    }

    // 砂嵐（白黒のランダムな点。点の大きさを出すため小さく作って引きのばす）
    static Texture2D StaticTexture()
    {
        var tex = new Texture2D(160, 90, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
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

    // うっすら走査線（4 ドットに 1 本、暗い線）
    static Texture2D ScanlineTexture()
    {
        var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < 4; y++) tex.SetPixel(0, y, new Color(0, 0, 0, y == 0 ? 0.12f : 0f));
        tex.Apply();
        return tex;
    }

    // 火花（小さな四角が飛び散って消える）
    void Burst(Vector3 pos, Color color, int count, float size, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            var sq = Box(effects, color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            sq.sizeDelta = Vector2.one * size * Random.Range(0.6f, 1.2f);
            sq.position = pos;
            sq.GetComponent<Image>().raycastTarget = false;
            StartCoroutine(Spark(sq, Random.insideUnitCircle.normalized * speed * Random.Range(0.4f, 1f)));
        }
    }

    IEnumerator Spark(RectTransform sq, Vector2 vel)
    {
        var img = sq.GetComponent<Image>();
        var c0 = img.color;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
        {
            sq.position += (Vector3)(vel * Time.deltaTime * (1f - t));
            img.color = new Color(c0.r, c0.g, c0.b, 1f - t);
            yield return null;
        }
        Destroy(sq.gameObject);
    }

    void ShowPopup(Hit h)
    {
        if (!cards.TryGetValue(h.Target, out var card)) return;
        var t = MakeText(card.Rect, (h.Heal ? "+" : "-") + h.Amount, 44, TextAnchor.MiddleCenter);
        t.color = h.Heal ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.4f, 0.3f);
        t.fontStyle = FontStyle.Bold;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.6f);
        rt.sizeDelta = new Vector2(200, 60);
        StartCoroutine(Popup(rt, t));
    }

    IEnumerator Popup(RectTransform rt, Text t)
    {
        float time = 0f;
        var start = rt.anchoredPosition;
        while (time < 0.9f)
        {
            time += Time.deltaTime;
            rt.anchoredPosition = start + new Vector2(0, time * 60f);
            var c = t.color; c.a = 1f - time / 0.9f; t.color = c;
            yield return null;
        }
        Destroy(rt.gameObject);
    }

    // ================= UI の組み立て =================

    void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
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

        // 背景：パソコンの中（電子回路の方眼）
        var bg = new GameObject("Background", typeof(RectTransform), typeof(RawImage));
        var bgRt = (RectTransform)bg.transform;
        bgRt.SetParent(root, false);
        Place(bgRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var raw = bg.GetComponent<RawImage>();
        raw.texture = CircuitTexture();
        raw.uvRect = new Rect(0, 0, 1920f / 64f, 1080f / 64f);
        raw.raycastTarget = false;

        cardLayer = Box(root, new Color(0, 0, 0, 0), Vector2.zero, Vector2.one);
        cardLayer.name = "Cards";
        cardLayer.GetComponent<Image>().raycastTarget = false;

        // 上の帯
        var header = Box(root, new Color(0, 0, 0, 0.4f), new Vector2(0, 0.93f), new Vector2(1, 1));
        turnText = MakeText(header, "", 34, TextAnchor.MiddleLeft);
        Place(turnText.rectTransform, new Vector2(0, 0), new Vector2(0.3f, 1), new Vector2(30, 0), Vector2.zero);
        messageText = MakeText(header, "", 30, TextAnchor.MiddleCenter);
        Place(messageText.rectTransform, new Vector2(0.25f, 0), new Vector2(0.75f, 1), Vector2.zero, Vector2.zero);
        var title = MakeText(header, "マジカルコンパイル", 30, TextAnchor.MiddleRight);
        title.color = new Color(1f, 0.6f, 0.85f);
        title.fontStyle = FontStyle.Bold;
        Place(title.rectTransform, new Vector2(0.7f, 0), new Vector2(1, 1), Vector2.zero, new Vector2(-30, 0));

        // タイムライン（左から右へ進み、右端に着いたキャラから行動する）
        var tl = Box(root, new Color(0, 0, 0, 0), new Vector2(0.05f, 0.30f), new Vector2(0.95f, 0.44f));
        var tlLabel = MakeText(tl, "タイムライン（右端に着いたら行動）", 22, TextAnchor.UpperLeft);
        Place(tlLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -30), new Vector2(0, 0));
        timelineBar = Box(tl, new Color(0.45f, 0.85f, 0.35f), new Vector2(0, 0.5f), new Vector2(1, 0.5f));
        timelineBar.sizeDelta = new Vector2(0, 14);
        var arrow = MakeText(timelineBar, "▶", 48, TextAnchor.MiddleLeft);
        arrow.color = new Color(0.45f, 0.85f, 0.35f);
        Place(arrow.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, -30), new Vector2(50, 30));

        // 下の帯：ログとボタン
        var bottom = Box(root, PanelColor, new Vector2(0, 0), new Vector2(1, 0.27f));
        logText = MakeText(bottom, "", 24, TextAnchor.UpperLeft);
        Place(logText.rectTransform, new Vector2(0, 0), new Vector2(0.65f, 1), new Vector2(30, 20), new Vector2(0, -20));
        programButton = MakeButton(bottom, "プログラムを組む", 32, OnProgramButton);
        Place((RectTransform)programButton.transform, new Vector2(0.68f, 0.55f), new Vector2(0.97f, 0.9f), Vector2.zero, Vector2.zero);
        startButton = MakeButton(bottom, "バトル開始", 32, OnStartButton);
        Place((RectTransform)startButton.transform, new Vector2(0.68f, 0.1f), new Vector2(0.97f, 0.45f), Vector2.zero, Vector2.zero);
        retryButton = MakeButton(bottom, "もう一度", 32, NewBattle);
        Place((RectTransform)retryButton.transform, new Vector2(0.68f, 0.1f), new Vector2(0.97f, 0.9f), Vector2.zero, Vector2.zero);

        effects = Box(root, new Color(0, 0, 0, 0), Vector2.zero, Vector2.one);
        effects.name = "Effects";
        effects.GetComponent<Image>().raycastTarget = false;

        // ノイズ：砂嵐のテクスチャ（ふだんは透明）と、うっすら走査線
        noiseLayer = Box(root, new Color(0, 0, 0, 0), Vector2.zero, Vector2.one);
        noiseLayer.name = "Noise";
        noiseLayer.GetComponent<Image>().raycastTarget = false;
        var scan = new GameObject("Scanlines", typeof(RectTransform), typeof(RawImage));
        var scanRt = (RectTransform)scan.transform;
        scanRt.SetParent(noiseLayer, false);
        Place(scanRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var scanImg = scan.GetComponent<RawImage>();
        scanImg.texture = ScanlineTexture();
        scanImg.uvRect = new Rect(0, 0, 1, 1080f / 4f);
        scanImg.raycastTarget = false;
        var nz = new GameObject("Static", typeof(RectTransform), typeof(RawImage));
        var nzRt = (RectTransform)nz.transform;
        nzRt.SetParent(noiseLayer, false);
        Place(nzRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        noise = nz.GetComponent<RawImage>();
        noise.texture = StaticTexture();
        noise.color = new Color(1, 1, 1, 0);
        noise.raycastTarget = false;
        BuildProgramScreen();
    }

    void BuildUnitViews()
    {
        foreach (var c in cards.Values) Destroy(c.Rect.gameObject);
        foreach (var i in icons.Values) Destroy(i.gameObject);
        cards.Clear();
        icons.Clear();

        for (int i = 0; i < battle.Allies.Count; i++)
            cards[battle.Allies[i]] = MakeCard(battle.Allies[i], AllyColors[i], new Vector2(0.03f + i * 0.145f, 0.47f), new Vector2(0.165f + i * 0.145f, 0.91f));
        for (int i = 0; i < battle.Enemies.Count; i++)
        {
            var e = battle.Enemies[i];
            cards[e] = MakeCard(e, EnemyColor, new Vector2(0.535f + i * 0.145f, 0.47f), new Vector2(0.67f + i * 0.145f, 0.91f));
            var btn = cards[e].Rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = cards[e].Rect.GetComponent<Image>();
            btn.onClick.AddListener(() => OnEnemyClicked(e));
        }

        // タイムラインのアイコン（味方はバーの上、敵は下）
        for (int i = 0; i < battle.Allies.Count; i++) icons[battle.Allies[i]] = MakeIcon(battle.Allies[i], AllyColors[i], 42f);
        foreach (var e in battle.Enemies) icons[e] = MakeIcon(e, EnemyColor, -42f);
    }

    UnitCard MakeCard(Unit u, Color color, Vector2 min, Vector2 max)
    {
        var rect = Box(cardLayer, color, min, max);
        rect.gameObject.AddComponent<CanvasGroup>();
        var frame = rect.gameObject.AddComponent<Outline>();
        frame.effectDistance = new Vector2(6, -6);
        frame.effectColor = new Color(0, 0, 0, 0);

        var name = MakeText(rect, u.Name, 32, TextAnchor.UpperCenter);
        Place(name.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -56), new Vector2(0, -10));
        name.fontStyle = FontStyle.Bold;

        // ドット絵
        var art = new GameObject("Art", typeof(RectTransform), typeof(Image));
        var artRt = (RectTransform)art.transform;
        artRt.SetParent(rect, false);
        Place(artRt, new Vector2(0, 0.40f), new Vector2(1, 0.88f), Vector2.zero, Vector2.zero);
        var artImg = art.GetComponent<Image>();
        artImg.sprite = PixelSprite(u.Art, false);
        artImg.preserveAspect = true;
        artImg.raycastTarget = false;

        var hpBack = Box(rect, new Color(0, 0, 0, 0.5f), new Vector2(0.08f, 0.33f), new Vector2(0.92f, 0.38f));
        var hpFill = Box(hpBack, new Color(0.4f, 1f, 0.5f), Vector2.zero, Vector2.one).GetComponent<Image>();
        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod = Image.FillMethod.Horizontal;
        hpFill.sprite = WhiteSprite();
        var hpText = MakeText(rect, "", 22, TextAnchor.MiddleCenter);
        Place(hpText.rectTransform, new Vector2(0, 0.25f), new Vector2(1, 0.33f), Vector2.zero, Vector2.zero);
        var info = MakeText(rect, "", 19, TextAnchor.UpperCenter);
        Place(info.rectTransform, new Vector2(0, 0), new Vector2(1, 0.25f), new Vector2(6, 4), new Vector2(-6, 0));

        var card = new UnitCard
        {
            Rect = rect, Frame = frame, HpFill = hpFill, HpText = hpText, InfoText = info,
            Art = artRt, ArtImage = artImg, Normal = artImg.sprite, Blink = u.IsAlly ? PixelSprite(u.Art, false, true) : null,
            Phase = Random.Range(0f, 6.28f), GlitchTimer = Random.Range(2f, 5f), BlinkTimer = Random.Range(1f, 4f), ShownHp = u.Hp,
        };
        return card;
    }

    RectTransform MakeIcon(Unit u, Color color, float y)
    {
        var icon = Box(timelineBar, color, new Vector2(0, 0.5f), new Vector2(0, 0.5f));
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.sizeDelta = new Vector2(60, 60);
        icon.anchoredPosition = new Vector2(0, y);
        icon.gameObject.AddComponent<Outline>().effectColor = Color.white;
        var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
        var faceRt = (RectTransform)face.transform;
        faceRt.SetParent(icon, false);
        Place(faceRt, Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
        var img = face.GetComponent<Image>();
        img.sprite = PixelSprite(u.Art, u.IsAlly); // 味方は顔だけ切り出す
        img.raycastTarget = false;
        return icon;
    }

    void BuildProgramScreen()
    {
        var screen = Box(root, new Color(0.10f, 0.10f, 0.16f, 0.97f), Vector2.zero, Vector2.one);
        programScreen = screen.gameObject;

        programTitle = MakeText(screen, "", 32, TextAnchor.MiddleLeft);
        Place(programTitle.rectTransform, new Vector2(0, 0.92f), new Vector2(0.6f, 1), new Vector2(30, 0), Vector2.zero);

        // ロボットを切りかえるタブ
        robotTabs.Clear();
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var b = MakeButton(screen, "", 26, () =>
            {
                var u = battle.Allies[idx];
                if (!u.Alive) return;
                editing = u;
                RefreshProgram();
            });
            Place((RectTransform)b.transform, new Vector2(0.02f + i * 0.13f, 0.84f), new Vector2(0.14f + i * 0.13f, 0.91f), Vector2.zero, Vector2.zero);
            robotTabs.Add((null, b.GetComponent<Image>(), b.GetComponentInChildren<Text>()));
        }

        // 手持ちブロック
        var palette = Box(screen, PanelColor, new Vector2(0.02f, 0.05f), new Vector2(0.27f, 0.82f));
        var pTitle = MakeText(palette, "手持ちブロック", 26, TextAnchor.MiddleCenter);
        Place(pTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -50), Vector2.zero);
        paletteList = Box(palette, new Color(0, 0, 0, 0), Vector2.zero, Vector2.one);
        paletteList.offsetMin = new Vector2(16, 16);
        paletteList.offsetMax = new Vector2(-16, -60);
        AddVertical(paletteList, 14);
        foreach (var k in Blocks.Order) MakeBlock(paletteList, new Block(k), true);
        var hint = MakeText(paletteList, "ドラッグして中央の\nコードに置こう", 20, TextAnchor.UpperLeft);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;

        // コード
        codeArea = Box(screen, new Color(0.85f, 0.80f, 0.92f), new Vector2(0.29f, 0.05f), new Vector2(0.66f, 0.82f));
        var cTitle = MakeText(codeArea, "コード", 26, TextAnchor.MiddleLeft);
        cTitle.color = new Color(0.2f, 0.2f, 0.3f);
        Place(cTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -50), Vector2.zero);
        codeList = Box(codeArea, new Color(0, 0, 0, 0), Vector2.zero, Vector2.one);
        codeList.offsetMin = new Vector2(30, 16);
        codeList.offsetMax = new Vector2(-30, -60);
        AddVertical(codeList, 8);
        MakeCapBlock(codeList, "スタート", "Start");
        MakeCapBlock(codeList, "フィニッシュ", "Finish");

        // 右側：メモリ・処理速度・送信
        var side = Box(screen, PanelColor, new Vector2(0.68f, 0.05f), new Vector2(0.98f, 0.82f));
        memoryText = MakeText(side, "", 30, TextAnchor.MiddleLeft);
        Place(memoryText.rectTransform, new Vector2(0, 0.88f), new Vector2(1, 0.98f), new Vector2(20, 0), new Vector2(-20, 0));
        var memBack = Box(side, new Color(0, 0, 0, 0.5f), new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.87f));
        memoryFill = Box(memBack, Color.cyan, Vector2.zero, Vector2.one).GetComponent<Image>();
        memoryFill.type = Image.Type.Filled;
        memoryFill.fillMethod = Image.FillMethod.Horizontal;
        memoryFill.sprite = WhiteSprite();
        speedText = MakeText(side, "", 30, TextAnchor.MiddleLeft);
        Place(speedText.rectTransform, new Vector2(0, 0.70f), new Vector2(1, 0.80f), new Vector2(20, 0), new Vector2(-20, 0));
        previewText = MakeText(side, "", 22, TextAnchor.UpperLeft);
        Place(previewText.rectTransform, new Vector2(0, 0.52f), new Vector2(1, 0.69f), new Vector2(20, 0), new Vector2(-20, 0));
        var rule = MakeText(side, "並べる順番：属性 → 対象 → 威力 → 発射方法 → 追加効果（無くてもよい）\nメモリを使うほど処理速度が遅くなる", 20, TextAnchor.UpperLeft);
        rule.color = new Color(0.8f, 0.8f, 0.9f);
        Place(rule.rectTransform, new Vector2(0, 0.36f), new Vector2(1, 0.51f), new Vector2(20, 0), new Vector2(-20, 0));
        programMessage = MakeText(side, "", 22, TextAnchor.UpperLeft);
        Place(programMessage.rectTransform, new Vector2(0, 0.25f), new Vector2(1, 0.36f), new Vector2(20, 0), new Vector2(-20, 0));
        var send = MakeButton(side, "送信", 36, OnSend);
        Place((RectTransform)send.transform, new Vector2(0.05f, 0.13f), new Vector2(0.95f, 0.24f), Vector2.zero, Vector2.zero);
        var close = MakeButton(side, "バトル画面へ", 28, () => { programScreen.SetActive(false); RefreshBattle(); });
        Place((RectTransform)close.transform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.11f), Vector2.zero, Vector2.zero);

        programScreen.SetActive(false);
    }

    void MakeCapBlock(RectTransform parent, string label, string name)
    {
        var cap = Box(parent, new Color(0.25f, 0.70f, 0.55f), Vector2.zero, Vector2.one);
        cap.name = name;
        cap.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
        var t = MakeText(cap, label, 26, TextAnchor.MiddleLeft);
        Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 0), Vector2.zero);
        t.fontStyle = FontStyle.Bold;
        cap.GetComponent<Image>().raycastTarget = false;
    }

    void MakeBlock(RectTransform parent, Block data, bool fromPalette)
    {
        var rect = Box(parent, BlockColors[(int)data.Kind], Vector2.zero, Vector2.one);
        rect.name = "Block";
        var le = rect.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 64;
        var view = rect.gameObject.AddComponent<BlockView>();
        view.Init(this, data, fromPalette, canvas);
        // 「何を」入れる白い枠（クリックで中身が変わる）
        var value = MakeButton(rect, "", 26, () => OnBlockValueClicked(view));
        var valueText = value.GetComponentInChildren<Text>();
        valueText.color = new Color(0.15f, 0.15f, 0.2f);
        Place((RectTransform)value.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(14, 8), new Vector2(124, -8));
        var suffix = MakeText(rect, "", 24, TextAnchor.MiddleLeft);
        Place(suffix.rectTransform, Vector2.zero, Vector2.one, new Vector2(136, 0), Vector2.zero);
        view.SetLabels(valueText, suffix);
    }

    // ================= ドット絵 =================

    static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    // PixelArt.cs の文字の並びから絵を作る。head = true なら顔のまわりだけ切り出す
    static Sprite PixelSprite(string name, bool head, bool blink = false)
    {
        string key = name + (head ? ":head" : "") + (blink ? ":blink" : "");
        if (spriteCache.TryGetValue(key, out var cached)) return cached;
        var rows = PixelArt.Sprites[name];
        if (blink) rows = CloseEyes(rows);
        var pal = new Dictionary<char, Color>();
        foreach (var kv in PixelArt.Common) pal[kv.Key] = Hex(kv.Value);
        foreach (var kv in PixelArt.Palettes[name]) pal[kv.Key] = Hex(kv.Value);
        int h = rows.Length, w = rows[0].Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;   // ドットをぼかさない
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = rows[y][x];
                tex.SetPixel(x, h - 1 - y, c == '.' || !pal.ContainsKey(c) ? new Color(0, 0, 0, 0) : pal[c]);
            }
        tex.Apply();
        var r = head ? new Rect(4, h - 24, 24, 24) : new Rect(0, 0, w, h);
        var sp = Sprite.Create(tex, r, new Vector2(0.5f, 0.5f), 32f);
        spriteCache[key] = sp;
        return sp;
    }

    // まばたき用：目（13〜14 行目の目の色）を肌にして、14 行目に閉じた目の線を引く
    static string[] CloseEyes(string[] rows)
    {
        var r = (string[])rows.Clone();
        var r12 = r[12].ToCharArray();
        var r13 = r[13].ToCharArray();
        var r14 = r[14].ToCharArray();
        foreach (int ex in new[] { 11, 19 })
            for (int x = ex; x <= ex + 2; x++)
            {
                r12[x] = 'S';
                r13[x] = 'S';
                r14[x] = 'K';
            }
        r[12] = new string(r12);
        r[13] = new string(r13);
        r[14] = new string(r14);
        return r;
    }

    static Color Hex(string s)
    {
        int v = System.Convert.ToInt32(s, 16);
        return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }

    // 背景の方眼（64×64 をくり返す。細い線と、ところどころ回路の点）
    static Texture2D CircuitTexture()
    {
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;
        var bg = Background;
        var line = new Color(0.08f, 0.16f, 0.26f);
        var dot = new Color(0.15f, 0.45f, 0.55f);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                var c = bg;
                if (x == 0 || y == 0) c = line;
                if ((x == 32 && y < 32) || (y == 40 && x > 32)) c = line;
                if ((x >= 30 && x <= 34 && y >= 30 && y <= 34) || (x >= 46 && x <= 48 && y >= 39 && y <= 41)) c = dot;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    // ================= 部品 =================

    static Sprite whiteSprite;
    static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        return whiteSprite;
    }

    static RectTransform Box(Transform parent, Color color, Vector2 min, Vector2 max)
    {
        var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
    }

    static void AddVertical(RectTransform rt, float spacing)
    {
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
        rt.GetComponent<Image>().raycastTarget = false;
    }

    Text MakeText(Transform parent, string s, int size, TextAnchor anchor)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = Color.white;
        t.text = s;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    Button MakeButton(Transform parent, string s, int size, UnityEngine.Events.UnityAction onClick)
    {
        var rt = Box(parent, ButtonColor, Vector2.zero, Vector2.one);
        rt.name = "Button";
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        b.onClick.AddListener(onClick);
        var t = MakeText(rt, s, size, TextAnchor.MiddleCenter);
        t.color = new Color(0.1f, 0.1f, 0.2f);
        return b;
    }
}

// ドラッグできるブロック。ドラッグ中は半透明の写しがマウスについてくる
public class BlockView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Block Data { get; private set; }
    public bool FromPalette { get; private set; }
    MagicGame game;
    Canvas canvas;
    RectTransform ghost;
    Text valueText, suffixText;

    public void SetLabels(Text value, Text suffix)
    {
        valueText = value;
        suffixText = suffix;
        RefreshLabels();
    }

    // 白い枠の中身と、右側の文字（かっこの中は使うメモリ）
    public void RefreshLabels()
    {
        valueText.text = Blocks.Label(Data.Kind, Data.Value);
        suffixText.text = Blocks.Suffix(Data.Kind) + "　(" + Blocks.Memory(Data.Kind, Data.Value) + ")";
    }

    public void Init(MagicGame g, Block data, bool fromPalette, Canvas c)
    {
        game = g;
        Data = data;
        FromPalette = fromPalette;
        canvas = c;
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
        cg.alpha = 0.8f;
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
