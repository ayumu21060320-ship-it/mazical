// ホーム画面と、そこから開く画面
//   ホーム:       タイトルの「スタート」のあと。依頼・チーム編成・図鑑・メールボックスを選ぶ。右上の歯車で遊び方
//   チーム編成:   出撃メンバーと装備（MagicGame.Team.cs）
//   ブロック図鑑: プログラムのブロックの説明。リンク専用と、まだ使えないブロック（条件分岐・繰り返し）も載せる
//   遊び方:       ページをめくって読む説明
//   ハッキング:   依頼を選んだあと、相手のパソコンにつないで直そうとするが、ウイルスがエラーを起こして抵抗し、戦闘になる

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MagicBattle;

public partial class MagicGame
{
    RectTransform homeLayer, bookLayer, howToLayer, virusLayer;
    Text homeBadgeText, mailBadgeText;
    GameObject homeBadge, mailBadge;
    Text howToTitle, howToBody, howToPage;
    RectTransform howToPicture;
    int howToIndex;

    // 画面をぜんぶ隠す（どれか 1 つを出す前に呼ぶ）
    void HideScreens()
    {
        titleLayer.gameObject.SetActive(false);
        battleLayer.gameObject.SetActive(false);
        requestLayer.gameObject.SetActive(false);
        homeLayer.gameObject.SetActive(false);
        teamLayer.gameObject.SetActive(false);
        loadoutLayer.gameObject.SetActive(false);
        bookLayer.gameObject.SetActive(false);
        virusLayer.gameObject.SetActive(false);
        mailLayer.gameObject.SetActive(false);
        howToLayer.gameObject.SetActive(false);
        programScreen.SetActive(false);
    }

    // 画面の背景（ピンク〜水色のグラデーションと魔法陣）と、左上の青い見出し
    RectTransform MenuScreen(string name, string heading)
    {
        var layer = Layer(name);
        var bg = layer.gameObject.AddComponent<RawImage>();
        bg.texture = UiKit.DiagonalGradient(UiKit.Hex("ffc3e6"), UiKit.Hex("b4e0ff"));
        var mc = Img(layer, UiKit.MagicCircle(), new Color(1, 1, 1, 0.3f)).rectTransform;
        mc.anchorMin = mc.anchorMax = new Vector2(0.86f, 0.18f);
        mc.sizeDelta = new Vector2(760, 760);
        ambient.StartCoroutine(SpinForever(mc, 4f));
        var header = Img(layer, UiKit.Slant(), UiKit.Hex("1e5cf0"));
        header.type = Image.Type.Sliced;
        Anchor(header.rectTransform, -0.02f, 0.88f, 0.3f, 0.97f);
        var ht = MakeText(header.rectTransform, heading, 56, Color.white, TextAnchor.MiddleCenter, true);
        ht.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(ht, UiKit.Hex("0a1c60"), 3f);
        layer.gameObject.SetActive(false);
        return layer;
    }

    // ================= ホーム =================

    void BuildHome()
    {
        homeLayer = MenuScreen("Home", "ホーム");

        // 味方の 3 人（舞台の上）
        var stageImg = Img(homeLayer, UiKit.Glow(), new Color(1f, 1f, 1f, 0.6f)).rectTransform;
        stageImg.anchorMin = stageImg.anchorMax = new Vector2(0.27f, 0.2f);
        stageImg.sizeDelta = new Vector2(900, 160);
        var xs = new[] { 0.13f, 0.27f, 0.41f };
        for (int i = 0; i < Characters.All.Length && i < xs.Length; i++)
        {
            var c = Characters.All[i];
            var img = Img(homeLayer, UiKit.Art(c.Art + "_idle"), Color.white);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(xs[i], i == 1 ? 0.17f : 0.2f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = UiKit.ArtSize(c.Art + "_idle") * 7.5f;
            ambient.StartCoroutine(TitleBob(rt, img, c.Art + "_idle", 7.5f));
            var name = MakeText(rt, c.Name, 30, Color.white, TextAnchor.MiddleCenter, true);
            Anchor(name.rectTransform, -0.5f, 0, 1.5f, 0, 0, -50, 0, 0);
            AddOutline(name, Purple, 3f);
        }
        var hello = Img(homeLayer, UiKit.RoundRect(30), new Color(0.12f, 0.1f, 0.16f, 0.85f));
        hello.type = Image.Type.Sliced;
        Anchor(hello.rectTransform, 0.05f, 0.74f, 0.5f, 0.85f);
        MakeText(hello.rectTransform, "依頼が来てるよ！ 今日はどのパソコンを助ける？", 28, Color.white, TextAnchor.MiddleCenter, true);

        // メニュー（右側の大きな帯）
        var items = new (string label, string sub, UnityEngine.Events.UnityAction act)[]
        {
            ("依頼", "ウイルスに困っている人を助ける", ShowRequests),
            ("チーム編成", "出撃メンバーと装備を決める", ShowTeam),
            ("ブロック図鑑", "プログラムのブロックを調べる", ShowBlockBook),
            ("ウイルス図鑑", "弱点・耐性・特性を調べる", ShowVirusBook),
            ("メールボックス", "依頼主からのお礼のメール", ShowMailbox),
            ("タイトルへ", "", ShowTitle),
        };
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            bool main = i == 0;
            var b = CommandButton(homeLayer, it.label, main ? 52 : 40, it.act, main);
            var rt = (RectTransform)b.transform;
            bool last = i == items.Length - 1;
            float top = 0.84f - i * 0.125f;
            Anchor(rt, 0.56f + i * 0.01f, top - (last ? 0.07f : 0.1f), 0.95f, top);
            if (it.sub != "")
            {
                var sub = MakeText(rt, it.sub, 22, main ? Color.white : UiKit.Hex("a8f4ff"), TextAnchor.LowerRight, false);
                Anchor(sub.rectTransform, 0, 0, 1, 1, 0, 8, -40, 0);
            }
            if (it.label == "メールボックス")
            {
                var mb = Img(rt, UiKit.Circle(), UiKit.Hex("e8283c"));
                mb.rectTransform.anchorMin = mb.rectTransform.anchorMax = new Vector2(0.97f, 0.95f);
                mb.rectTransform.sizeDelta = new Vector2(48, 48);
                var mol = mb.gameObject.AddComponent<Outline>();
                mol.effectColor = Color.white;
                mol.effectDistance = new Vector2(3, -3);
                mailBadgeText = MakeText(mb.rectTransform, "", 28, Color.white, TextAnchor.MiddleCenter, true);
                mailBadge = mb.gameObject;
            }
            if (main)
            {
                var badge = Img(rt, UiKit.Circle(), UiKit.Hex("e8283c"));
                badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0.97f, 0.95f);
                badge.rectTransform.sizeDelta = new Vector2(56, 56);
                var ol = badge.gameObject.AddComponent<Outline>();
                ol.effectColor = Color.white;
                ol.effectDistance = new Vector2(3, -3);
                homeBadgeText = MakeText(badge.rectTransform, "", 34, Color.white, TextAnchor.MiddleCenter, true);
                homeBadge = badge.gameObject;
            }
        }

        // 歯車（遊び方）
        var gear = Img(homeLayer, UiKit.Circle(), Color.white);
        gear.raycastTarget = true;
        gear.rectTransform.anchorMin = gear.rectTransform.anchorMax = new Vector2(0.96f, 0.93f);
        gear.rectTransform.sizeDelta = new Vector2(96, 96);
        var gearIcon = NewRect(gear.rectTransform, "Gear");
        Anchor(gearIcon, 0, 0, 1, 1, 8, 8, -8, -8);
        IconGear(gearIcon);
        var gb = gear.gameObject.AddComponent<Button>();
        gb.targetGraphic = gear;
        gb.onClick.AddListener(() => ShowHowTo(0));
        var gl = MakeText(homeLayer, "遊び方", 24, Ink, TextAnchor.MiddleCenter, true);
        Anchor(gl.rectTransform, 0.92f, 0.83f, 1f, 0.88f);
    }

    void ShowHome()
    {
        StopAllCoroutines();
        ClearChildren(effects);
        phase = Phase.Home;
        HideScreens();
        homeLayer.gameObject.SetActive(true);
        int n = 0;
        foreach (var r in Requests.All) if (!IsCleared(r)) n++;
        homeBadge.SetActive(n > 0);
        homeBadgeText.text = n.ToString();
        int mailsNew = UnreadMails();
        mailBadge.SetActive(mailsNew > 0);
        mailBadgeText.text = mailsNew.ToString();
        if (mailsNew > 0) StartCoroutine(Toast(homeLayer, "新しいメールが " + mailsNew + " 件あります"));
    }

    // ================= キャラクターの特性の文字 =================

    static string TraitText(RobotInfo c) =>
        c.Trait == Trait.FireUp ? "炎属性 火力UP" : c.Trait == Trait.IceUp ? "氷属性 火力UP" : "メモリ容量UP";

    // ================= ブロック図鑑 =================

    void BuildBlockBook()
    {
        bookLayer = MenuScreen("BlockBook", "ブロック図鑑");
        var list = NewRect(bookLayer, "List");
        Anchor(list, 0.03f, 0.14f, 0.97f, 0.86f);
        var entries = new (int color, string name, string desc, string tag)[]
        {
            (0, "属性を選択", "炎・氷・無から選ぶ。敵の弱点の属性なら威力 1.5 倍。リンクでは 2 つ置ける", ""),
            (1, "対象に攻撃", "1体（ねらった敵）か全体。全体はメモリを多く使う", ""),
            (2, "威力", "10〜120。大きいほどメモリを使う。リンクでは 200・400 も選べる", ""),
            (3, "発射方法（回打つ）", "1〜3 回。回数が多いほどメモリを使う", ""),
            (4, "追加効果を得る", "爆破（威力 1.3 倍）・回復（自分の HP）・スロー（敵のタイムラインを戻す）。無くてもよい", ""),
            (5, "マジカルリンク", "このブロックだけで使えるメモリを全部使う。それ1つで、2人の得意な属性で敵全体へコンビ魔法", "リンク専用"),
            (-2, "もし〜なら（条件分岐）", "条件（敵の弱点・敵の数・自分のHP・敵のHP・敵の特性）で中のブロックを切りかえる", "装備で使える"),
            (-3, "〜回繰り返す", "中の「発射方法」を決めた回数だけくり返す。メモリは1回分ですむ", "装備で使える"),
        };
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            int col = i % 2, row = i / 2;
            var cell = Img(list, UiKit.RoundRect(18), new Color(1, 1, 1, 0.92f));
            cell.type = Image.Type.Sliced;
            Anchor(cell.rectTransform, col * 0.5f + 0.005f, 1f - (row + 1) * 0.25f + 0.008f, (col + 1) * 0.5f - 0.005f, 1f - row * 0.25f - 0.008f);
            var block = Img(cell.rectTransform, UiKit.BlockShape(), e.color >= 0 ? BlockColors[e.color] : e.color == -2 ? UiKit.Hex("ffab19") : e.color == -3 ? UiKit.Hex("f0801e") : UiKit.Hex("9a9eb0"));
            block.type = Image.Type.Sliced;
            Anchor(block.rectTransform, 0, 0.5f, 0, 0.5f, 20, -36, 330, 30);
            var bn = MakeText(block.rectTransform, e.name, 24, Color.white, TextAnchor.MiddleLeft, true);
            Anchor(bn.rectTransform, 0, 0, 1, 1, 18, UiKit.BlockTabHeight, 0, 0);
            var desc = MakeText(cell.rectTransform, e.desc, 22, Ink, TextAnchor.MiddleLeft, false);
            Anchor(desc.rectTransform, 0, 0, 1, 1, 350, 6, -16, -6);
            if (e.tag != "")
            {
                var tag = Img(cell.rectTransform, UiKit.RoundRect(10), e.color >= 0 ? Pink : UiKit.Hex("f0801e"));
                tag.type = Image.Type.Sliced;
                Anchor(tag.rectTransform, 1, 1, 1, 1, -170, -40, -12, -8);
                MakeText(tag.rectTransform, e.tag, 20, Color.white, TextAnchor.MiddleCenter, true);
            }
        }
        var back = CommandButton(bookLayer, "ホームへ", 32, ShowHome, false);
        Anchor((RectTransform)back.transform, 0.03f, 0.03f, 0.2f, 0.11f);
    }

    void ShowBlockBook()
    {
        HideScreens();
        bookLayer.gameObject.SetActive(true);
    }

    // ================= ウイルス図鑑 =================

    void BuildVirusBook()
    {
        virusLayer = MenuScreen("VirusBook", "ウイルス図鑑");
        var list = NewRect(virusLayer, "List");
        Anchor(list, 0.03f, 0.14f, 0.97f, 0.86f);
        int n = Viruses.All.Length;
        for (int i = 0; i < n; i++)
        {
            var v = Viruses.All[i];
            var border = Img(list, UiKit.RoundRect(26), Magenta);
            border.type = Image.Type.Sliced;
            Anchor(border.rectTransform, i / (float)n + 0.005f, 0, (i + 1) / (float)n - 0.005f, 1);
            var card = Img(border.rectTransform, UiKit.RoundRect(22), new Color(0.1f, 0.07f, 0.18f, 0.95f));
            card.type = Image.Type.Sliced;
            var c = card.rectTransform;
            Anchor(c, 0, 0, 1, 1, 5, 5, -5, -5);
            var art = Img(c, UiKit.Art(v.Art), Color.white);
            art.preserveAspect = true;
            Anchor(art.rectTransform, 0.15f, 0.66f, 0.85f, 0.97f);
            ambient.StartCoroutine(Bob(art.rectTransform, i));
            var name = MakeText(c, v.Name, 38, Magenta, TextAnchor.MiddleCenter, true);
            Anchor(name.rectTransform, 0, 0.58f, 1, 0.66f);
            var stats = MakeText(c,
                "HP " + v.Hp + "　攻撃 " + v.Attack + "\n<color=#ffd84a>弱点 " + Elements(v.Weak) + "</color>\n<color=#a8b0c8>耐性 " + Elements(v.Resist) + "</color>",
                23, Color.white, TextAnchor.UpperCenter, true);
            stats.supportRichText = true;
            Anchor(stats.rectTransform, 0.04f, 0.4f, 0.96f, 0.58f);
            var ab = MakeText(c, "<color=#ff8ae0>特性：" + Unit.AbilityName(v.Ability) + "</color>\n" + Unit.AbilityText(v.Ability), 20, Color.white, TextAnchor.UpperLeft, true);
            ab.supportRichText = true;
            Anchor(ab.rectTransform, 0.07f, 0.2f, 0.93f, 0.4f);
            var desc = MakeText(c, v.Description, 19, UiKit.Hex("c8c0e0"), TextAnchor.UpperLeft, false);
            Anchor(desc.rectTransform, 0.07f, 0.02f, 0.93f, 0.2f);
        }
        var back = CommandButton(virusLayer, "ホームへ", 32, ShowHome, false);
        Anchor((RectTransform)back.transform, 0.03f, 0.03f, 0.2f, 0.11f);
        var hint = MakeText(virusLayer, "弱点の属性は 1.5 倍、耐性の属性は 0.5 倍。特性に合わせてプログラムを組もう", 26, Ink, TextAnchor.MiddleLeft, true);
        Anchor(hint.rectTransform, 0.23f, 0.03f, 0.97f, 0.11f);
    }

    IEnumerator Bob(RectTransform rt, int i)
    {
        while (rt != null)
        {
            rt.anchoredPosition = new Vector2(0, Mathf.Sin(Time.time * 2f + i) * 8f);
            yield return null;
        }
    }

    void ShowVirusBook()
    {
        HideScreens();
        virusLayer.gameObject.SetActive(true);
    }

    // ================= 遊び方 =================

    static readonly (string title, string body, string picture)[] HowToPages =
    {
        ("このゲームの目的",
         "あなたはプログラマー。ウイルスに困っている人のパソコンにリモートでつないで、\nAI ロボットたちに魔法のプログラムを送り、ウイルスを撃退しよう。\n\nウイルスを全部倒せば勝ち。ロボットが全員倒れると負け。", "robots"),
        ("プログラムを組む",
         "戦闘画面で味方をクリックして選び、「プログラム」を押す。\n左の手持ちブロックを、右のコードへドラッグして並べる。\n\n並べる順番：属性 → 対象 → 威力 → 発射方法 → 追加効果（無くてもよい）\nブロックの ▼ をクリックすると中身が変わる。できたら「送信」！", "blocks"),
        ("メモリと処理速度",
         "ブロックはメモリを使う。メモリ容量をこえると送信できない。\nメモリをたくさん使うほど処理速度が遅くなり、行動が遅くなる。\n\n弱い魔法を何回も打つか、強い魔法をじっくり打つか、作戦を考えよう。", "memory"),
        ("タイムライン",
         "「バトル開始」でタイムラインが進む。右端に着いたキャラから行動する。\n全員が 1 回以上行動するとターンが終わり、またプログラムを組み直せる。\n\n速いキャラは、同じターンに 2 回行動できることもある。", "timeline"),
        ("ウイルスの弱点・耐性・特性",
         "ウイルスには弱点（1.5 倍効く属性）と耐性（0.5 倍しか効かない属性）がある。\n敵の頭の上や、ホームの「ウイルス図鑑」で確かめよう。\n\n増殖・ロック・分散などの特性もある。弱点をつくと「WEAK!」と出る。", "virus"),
        ("もし〜なら・〜回繰り返す",
         "チーム編成の「装備」で追加ブロックをはめると、そのロボットのプログラムで使える。\n\nもし ＜条件＞ なら … でなければ … ：条件で中のブロックを切りかえる。\n〜回繰り返す … ：中の「発射方法」をその回数だけくり返す。メモリは1回分！", "blocks"),
        ("チャージとリンクプログラム",
         "ガード：そのターンは魔法を打たず、受けるダメージが半分。\nチャージ：そのターンは魔法を打たず、次のターンのメモリと攻撃力が 1.3 倍。\n\nリンクプログラム（1 試合 2 回）：ペアを選んで 2 人分のメモリで魔法を組む。\nマジカルリンクのブロック1つで、2人のコンビ魔法が完成する切り札！", "link"),
    };

    void BuildHowTo()
    {
        howToLayer = NewRect(root, "HowTo");
        Fill(howToLayer);
        var dim = howToLayer.gameObject.AddComponent<Image>();
        dim.color = new Color(0, 0, 0, 0.55f);
        var border = Img(howToLayer, UiKit.RoundRect(30), Purple);
        border.type = Image.Type.Sliced;
        Anchor(border.rectTransform, 0.12f, 0.1f, 0.88f, 0.9f);
        var win = Img(border.rectTransform, UiKit.RoundRect(26), UiKit.Hex("f7eef8"));
        win.type = Image.Type.Sliced;
        Anchor(win.rectTransform, 0, 0, 1, 1, 6, 6, -6, -6);
        var bar = Img(win.rectTransform, UiKit.RoundRect(26), Pink);
        bar.type = Image.Type.Sliced;
        Anchor(bar.rectTransform, 0, 1, 1, 1, 0, -70, 0, 0);
        MakeText(bar.rectTransform, "　遊び方", 34, Color.white, TextAnchor.MiddleLeft, true);
        howToPage = MakeText(bar.rectTransform, "", 28, Color.white, TextAnchor.MiddleRight, true);
        Anchor(howToPage.rectTransform, 0, 0, 1, 1, 0, 0, -30, 0);
        howToTitle = MakeText(win.rectTransform, "", 46, Purple, TextAnchor.MiddleLeft, true);
        Anchor(howToTitle.rectTransform, 0, 1, 1, 1, 50, -160, -40, -90);
        howToPicture = NewRect(win.rectTransform, "Picture");
        Anchor(howToPicture, 0.04f, 0.22f, 0.38f, 0.78f);
        howToBody = MakeText(win.rectTransform, "", 28, Ink, TextAnchor.UpperLeft, false);
        Anchor(howToBody.rectTransform, 0.4f, 0.2f, 0.96f, 0.78f);
        var prev = CommandButton(win.rectTransform, "◀ まえ", 30, () => ShowHowTo(howToIndex - 1), false);
        Anchor((RectTransform)prev.transform, 0.04f, 0.04f, 0.24f, 0.13f);
        var next = CommandButton(win.rectTransform, "つぎ ▶", 30, () => ShowHowTo(howToIndex + 1), false);
        Anchor((RectTransform)next.transform, 0.52f, 0.04f, 0.72f, 0.13f);
        var close = CommandButton(win.rectTransform, "とじる", 30, () => howToLayer.gameObject.SetActive(false), true);
        Anchor((RectTransform)close.transform, 0.76f, 0.04f, 0.96f, 0.13f);
        howToLayer.gameObject.SetActive(false);
    }

    void ShowHowTo(int index)
    {
        howToIndex = Mathf.Clamp(index, 0, HowToPages.Length - 1);
        var p = HowToPages[howToIndex];
        howToLayer.gameObject.SetActive(true);
        howToLayer.SetAsLastSibling();
        howToTitle.text = p.title;
        howToBody.text = p.body;
        howToPage.text = (howToIndex + 1) + " / " + HowToPages.Length;
        ClearChildren(howToPicture);
        switch (p.picture)
        {
            case "robots":
                for (int i = 0; i < Characters.All.Length; i++)
                {
                    var img = Img(howToPicture, UiKit.Art(Characters.All[i].Art + "_idle"), Color.white);
                    img.preserveAspect = true;
                    Anchor(img.rectTransform, i / 3f, 0.1f, (i + 1) / 3f, 0.9f);
                }
                break;
            case "blocks":
                for (int i = 0; i < 5; i++)
                {
                    var b = Img(howToPicture, UiKit.BlockShape(), BlockColors[i]);
                    b.type = Image.Type.Sliced;
                    Anchor(b.rectTransform, 0.05f, 0.85f - i * 0.16f, 0.95f, 0.98f - i * 0.16f);
                    var t = MakeText(b.rectTransform, Blocks.KindName(Blocks.Order[i]), 24, Color.white, TextAnchor.MiddleLeft, true);
                    Anchor(t.rectTransform, 0, 0, 1, 1, 20, UiKit.BlockTabHeight, 0, 0);
                }
                break;
            case "memory":
                var back = Img(howToPicture, UiKit.RoundRect(12), UiKit.Hex("d8d0e8"));
                back.type = Image.Type.Sliced;
                Anchor(back.rectTransform, 0.05f, 0.55f, 0.95f, 0.62f);
                var fill = Img(back.rectTransform, UiKit.RoundRect(12), UiKit.Hex("3fb8ff"));
                fill.type = Image.Type.Sliced;
                Anchor(fill.rectTransform, 0, 0, 0.6f, 1);
                var m = MakeText(howToPicture, "メモリ　600 / 1000", 30, Ink, TextAnchor.MiddleCenter, true);
                Anchor(m.rectTransform, 0, 0.64f, 1, 0.8f);
                var sp = MakeText(howToPicture, "処理速度　ふつう", 34, UiKit.Hex("2a9a4a"), TextAnchor.MiddleCenter, true);
                Anchor(sp.rectTransform, 0, 0.3f, 1, 0.5f);
                break;
            case "timeline":
                var line = Img(howToPicture, null, UiKit.Hex("5ee05a"));
                Anchor(line.rectTransform, 0.05f, 0.48f, 0.85f, 0.52f);
                var head = Img(howToPicture, UiKit.Arrow(), UiKit.Hex("5ee05a"));
                Anchor(head.rectTransform, 0.85f, 0.42f, 0.97f, 0.58f);
                for (int i = 0; i < 3; i++)
                {
                    var face = Img(howToPicture, UiKit.Art(Characters.All[i].Art + "_idle", FaceCrop), Color.white);
                    face.preserveAspect = true;
                    Anchor(face.rectTransform, 0.1f + i * 0.28f, 0.55f, 0.28f + i * 0.28f, 0.75f);
                }
                var v = Img(howToPicture, UiKit.Art("bug"), Color.white);
                v.preserveAspect = true;
                Anchor(v.rectTransform, 0.45f, 0.25f, 0.62f, 0.45f);
                break;
            case "virus":
                var names = new[] { "bug", "spam", "ransom" };
                for (int i = 0; i < names.Length; i++)
                {
                    var img = Img(howToPicture, UiKit.Art(names[i]), Color.white);
                    img.preserveAspect = true;
                    Anchor(img.rectTransform, i / 3f, 0.35f, (i + 1) / 3f, 0.75f);
                }
                var wk = MakeText(howToPicture, "WEAK!", 56, UiKit.Hex("ffd84a"), TextAnchor.MiddleCenter, true);
                wk.fontStyle = FontStyle.BoldAndItalic;
                AddOutline(wk, Color.black, 3f);
                Anchor(wk.rectTransform, 0, 0.08f, 1, 0.3f);
                break;
            case "link":
                var a = Img(howToPicture, UiKit.Art("red_cast"), Color.white);
                a.preserveAspect = true;
                Anchor(a.rectTransform, 0f, 0.1f, 0.5f, 0.9f);
                var b2 = Img(howToPicture, UiKit.Art("blue_cast"), Color.white);
                b2.preserveAspect = true;
                Anchor(b2.rectTransform, 0.4f, 0.1f, 0.9f, 0.9f);
                var ring = Img(howToPicture, UiKit.MagicCircle(), new Color(1f, 0.4f, 0.85f, 0.6f));
                Anchor(ring.rectTransform, 0.1f, 0f, 0.8f, 0.25f);
                break;
        }
    }

    // ================= ハッキング（依頼主のパソコンにつなぐ） =================

    static readonly string[] HackCode =
    {
        "using MagicCompile;",
        "var target = Remote.Connect(\"{host}\");",
        "target.Firewall.Bypass(key: 0x4D43);",
        "Auth.Login(\"magical-compile\", token);",
        "foreach (var file in target.Scan(\"/\"))",
        "{",
        "    if (file.IsInfected) Quarantine(file);",
        "}",
        "Memory.Dump(0x00A0, 0x0FFF);",
        "Repair.Start(target.System);",
        "Repair.Patch(\"kernel.sys\");",
        "Repair.Restore(\"user/documents\");",
        "Process.Kill(\"???.exe\");",
    };

    static readonly string[] ErrorMessages =
    {
        "ERROR 0x0BAD : アクセスが拒否されました",
        "致命的なエラー：メモリが書きかえられています",
        "警告：不明なプロセスが増殖しています",
        "ERROR 0xDEAD : 修復プログラムが停止しました",
        "接続が不安定です（パケット損失 87%）",
        "ウイルスが修復をじゃましています！",
    };

    IEnumerator Connect(Request r)
    {
        connecting = true;
        var screen = Img(requestLayer, null, UiKit.Hex("07060f"));
        screen.raycastTarget = true;
        var sc = screen.rectTransform;
        sc.name = "Connect";
        Fill(sc);
        var grid = NewRect(sc, "Grid").gameObject.AddComponent<RawImage>();
        Fill(grid.rectTransform);
        grid.texture = CircuitTexture();
        grid.uvRect = new Rect(0, 0, 1920f / 96f, 1080f / 96f);
        grid.color = new Color(1, 1, 1, 0.25f);
        grid.raycastTarget = false;

        var head = MakeText(sc, r.Client + "さんのPC にリモート接続中", 40, Cyan, TextAnchor.MiddleLeft, true);
        Anchor(head.rectTransform, 0.04f, 0.88f, 0.96f, 0.96f);
        AddOutline(head, UiKit.Hex("0a3a48"), 3f);

        // 左：プレイヤーが打ちこむコード
        var codeBox = Img(sc, UiKit.RoundRect(14), new Color(0.03f, 0.05f, 0.08f, 0.92f));
        codeBox.type = Image.Type.Sliced;
        Anchor(codeBox.rectTransform, 0.04f, 0.08f, 0.62f, 0.86f);
        var codeBar = Img(codeBox.rectTransform, UiKit.RoundRect(14), UiKit.Hex("1e2a3a"));
        codeBar.type = Image.Type.Sliced;
        Anchor(codeBar.rectTransform, 0, 1, 1, 1, 0, -44, 0, 0);
        MakeText(codeBar.rectTransform, "　repair.cs ― あなたのコード", 22, UiKit.Hex("a8f4ff"), TextAnchor.MiddleLeft, true);
        var code = MakeText(codeBox.rectTransform, "", 26, UiKit.Hex("5ee05a"), TextAnchor.UpperLeft, false);
        code.supportRichText = true;
        Anchor(code.rectTransform, 0, 0, 1, 1, 24, 16, -16, -60);

        // 右：状態
        var side = Img(sc, UiKit.RoundRect(14), new Color(0.03f, 0.05f, 0.08f, 0.92f));
        side.type = Image.Type.Sliced;
        Anchor(side.rectTransform, 0.65f, 0.4f, 0.96f, 0.86f);
        var state = MakeText(side.rectTransform, "", 26, Color.white, TextAnchor.UpperLeft, true);
        state.supportRichText = true;
        Anchor(state.rectTransform, 0, 0, 1, 1, 24, 16, -16, -20);
        var barLabel = MakeText(sc, "修復プログラム", 26, Cyan, TextAnchor.MiddleLeft, true);
        Anchor(barLabel.rectTransform, 0.65f, 0.33f, 0.96f, 0.38f);
        var barBack = Img(sc, UiKit.RoundRect(12), new Color(1, 1, 1, 0.15f));
        barBack.type = Image.Type.Sliced;
        Anchor(barBack.rectTransform, 0.65f, 0.29f, 0.96f, 0.32f);
        var fill = Img(barBack.rectTransform, UiKit.RoundRect(12), Cyan);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        Fill(fill.rectTransform);
        var percent = MakeText(sc, "", 30, Color.white, TextAnchor.MiddleRight, true);
        Anchor(percent.rectTransform, 0.65f, 0.33f, 0.96f, 0.38f);

        string host = r.Id + "-pc";
        var lines = new List<string>();
        string typing = "";
        int lineIndex = 0;
        float progress = 0f;
        var errorBoxes = new List<GameObject>();

        // 1. プレイヤーがコードを打ちこんで、修復が進む
        float t1 = 0f;
        while (t1 < 3.0f)
        {
            t1 += Time.deltaTime;
            if (lineIndex < HackCode.Length)
            {
                string full = HackCode[lineIndex].Replace("{host}", host);
                int n = Mathf.Min(full.Length, typing.Length + Mathf.Max(1, (int)(Time.deltaTime * 70f)));
                typing = full.Substring(0, n);
                if (typing.Length >= full.Length) { lines.Add(full); typing = ""; lineIndex++; }
            }
            progress = Mathf.Min(0.72f, t1 / 3.0f * 0.72f);
            fill.fillAmount = progress;
            percent.text = (int)(progress * 100) + "%";
            code.text = CodeView(lines, typing + ((int)(Time.time * 4) % 2 == 0 ? "_" : ""));
            state.text = "接続　　　<color=#5ee05a>OK</color>\nファイアウォール　<color=#5ee05a>突破</color>\n認証　　　<color=#5ee05a>OK</color>\nスキャン　" + (int)(progress * 130) + "%\n修復　　　実行中…";
            yield return null;
        }

        // 2. ウイルスが抵抗：エラーのウィンドウが次々に出て、コードがこわれていく
        head.text = "ウイルスが抵抗しています！";
        head.color = Magenta;
        barLabel.text = "修復プログラム　<color=#ff4fd8>停止</color>";
        barLabel.supportRichText = true;
        fill.color = Magenta;
        float t2 = 0f, spawn = 0f;
        while (t2 < 2.4f)
        {
            t2 += Time.deltaTime;
            spawn -= Time.deltaTime;
            if (spawn <= 0f)
            {
                spawn = Mathf.Lerp(0.35f, 0.08f, t2 / 2.4f);
                errorBoxes.Add(ErrorWindow(sc, r));
                StartCoroutine(Noise(0.2f, 0.35f + t2 * 0.15f));
                // コードの行をこわす
                if (lines.Count > 0)
                {
                    int k = Random.Range(0, lines.Count);
                    lines[k] = "<color=#ff4f6a>" + Garble(StripTags(lines[k])) + "</color>";
                }
            }
            progress = Mathf.Max(0f, progress - Time.deltaTime * 0.25f + Random.Range(-0.02f, 0.02f));
            fill.fillAmount = progress;
            percent.text = "<color=#ff4fd8>" + (int)(progress * 100) + "%</color>";
            percent.supportRichText = true;
            code.text = CodeView(lines, Garble("Repair.Resume();"));
            state.text = "接続　　　<color=#ffd84a>不安定</color>\nファイアウォール　<color=#ff4f6a>書きかえ</color>\n認証　　　<color=#ff4f6a>エラー</color>\nウイルス　<color=#ff4f6a>" + r.Enemies.Length + " 件が攻撃中</color>\n修復　　　<color=#ff4f6a>停止</color>";
            sc.anchoredPosition = Random.insideUnitCircle * (4f + t2 * 6f);   // 画面がゆれる
            yield return null;
        }
        sc.anchoredPosition = Vector2.zero;

        // 3. ウイルスの姿があらわれて、戦闘へ
        var flash = Img(sc, null, new Color(0.7f, 0.02f, 0.15f, 0.85f)).rectTransform;
        Anchor(flash, 0, 0.36f, 1, 0.64f);
        var ft = MakeText(flash, "VIRUS ATTACK", 110, Color.white, TextAnchor.MiddleCenter, true);
        ft.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(ft, Color.black, 5f);
        for (int k = 0; k < r.Enemies.Length; k++)
        {
            var v = Img(sc, UiKit.Art(r.Enemies[k].Art), new Color(1, 1, 1, 0.9f));
            v.preserveAspect = true;
            float w = 0.7f / r.Enemies.Length;
            Anchor(v.rectTransform, 0.15f + k * w, 0.64f, 0.15f + (k + 1) * w, 0.86f);
        }
        StartCoroutine(Noise(0.7f, 0.8f));
        for (int i = 0; i < 8; i++)
        {
            flash.gameObject.SetActive(i % 2 == 0 || i > 4);
            flash.anchoredPosition = new Vector2(Random.Range(-40f, 40f), 0);
            yield return new WaitForSeconds(0.08f);
        }
        yield return new WaitForSeconds(0.5f);

        Destroy(screen.gameObject);
        connecting = false;
        requestLayer.gameObject.SetActive(false);
        battleLayer.gameObject.SetActive(true);
        NewBattle();
    }

    // 下の 16 行だけ見せる（行番号つき）
    static string CodeView(List<string> lines, string typing)
    {
        var sb = new System.Text.StringBuilder();
        int start = Mathf.Max(0, lines.Count + 1 - 16);
        for (int i = start; i < lines.Count; i++) sb.Append("<color=#4a5a6a>").Append((i + 1).ToString("00")).Append("</color>  ").Append(lines[i]).Append('\n');
        sb.Append("<color=#4a5a6a>").Append((lines.Count + 1).ToString("00")).Append("</color>  ").Append(typing);
        return sb.ToString();
    }

    static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");

    // 文字をこわす（ところどころ記号にする）
    static string Garble(string s)
    {
        const string junk = "#$%&@!?*01■□";
        var c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
            if (Random.value < 0.35f) c[i] = junk[Random.Range(0, junk.Length)];
        return new string(c).Replace("<", "＜").Replace(">", "＞");
    }

    // 赤いエラーのウィンドウ
    GameObject ErrorWindow(RectTransform parent, Request r)
    {
        var box = Img(parent, UiKit.RoundRect(10), new Color(0.96f, 0.94f, 0.96f, 0.97f));
        box.type = Image.Type.Sliced;
        var rt = box.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(Random.Range(0.15f, 0.85f), Random.Range(0.2f, 0.85f));
        rt.sizeDelta = new Vector2(Random.Range(460f, 560f), 150);
        var bar = Img(rt, UiKit.RoundRect(10), UiKit.Hex("d81e3c"));
        bar.type = Image.Type.Sliced;
        Anchor(bar.rectTransform, 0, 1, 1, 1, 0, -40, 0, 0);
        MakeText(bar.rectTransform, "　！ ERROR", 22, Color.white, TextAnchor.MiddleLeft, true);
        MakeText(bar.rectTransform, "×　", 24, Color.white, TextAnchor.MiddleRight, true);
        var msg = MakeText(rt, ErrorMessages[Random.Range(0, ErrorMessages.Length)], 24, Ink, TextAnchor.MiddleCenter, true);
        Anchor(msg.rectTransform, 0, 0, 1, 1, 16, 8, -16, -44);
        var virus = Img(rt, UiKit.Art(r.Enemies[Random.Range(0, r.Enemies.Length)].Art), Color.white);
        virus.preserveAspect = true;
        Anchor(virus.rectTransform, 0, 0, 0, 0, 8, 8, 64, 64);
        return box.gameObject;
    }
}
