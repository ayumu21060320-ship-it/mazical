// 成長とごほうび
//   経験値：依頼を駆除すると依頼主から EXP がもらえる（3人とも）。たまるとレベルが上がり、メモリ容量と HP が増える
//   依頼完了の通信：勝ったあと、依頼主とつながる通信画面でお礼の言葉 →「依頼完了」→ EXP とレベルアップ
//   メールボックス：はじめて駆除した依頼主からお礼のプレゼントメールが届く（2回目からはときどき）。
//                   開いて「受け取る」と EXP やメモリ増設チップがもらえる
// 保存は PlayerPrefs（xp_キャラ, chip_mem, mails）

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MagicBattle;

public partial class MagicGame
{
    // ================= 経験値・レベル =================

    static int GetXp(string art) => PlayerPrefs.GetInt("xp_" + art, 0);
    static int MemoryBonus => PlayerPrefs.GetInt("chip_mem", 0);

    static Dictionary<string, int> LevelMap()
    {
        var d = new Dictionary<string, int>();
        foreach (var c in Characters.All) d[c.Art] = Levels.LevelOf(GetXp(c.Art));
        return d;
    }

    // 3人に経験値をあげる。レベルが上がったキャラの名前を返す
    static List<string> GiveXp(int amount)
    {
        var up = new List<string>();
        foreach (var c in Characters.All)
        {
            int before = GetXp(c.Art);
            int after = before + amount;
            PlayerPrefs.SetInt("xp_" + c.Art, after);
            if (Levels.LevelOf(after) > Levels.LevelOf(before)) up.Add(c.Name);
        }
        PlayerPrefs.Save();
        return up;
    }

    // ================= メール =================

    class Mail
    {
        public string Id, From, Subject, Body, Reward;   // Reward: "xp" か "chip"（メモリ増設）か ""
        public int Amount;
        public bool Read, Claimed;
    }

    const char FieldSep = '\u001f', MailSep = '\u001e';

    static List<Mail> LoadMails()
    {
        var list = new List<Mail>();
        string raw = PlayerPrefs.GetString("mails", "");
        if (raw == "") return list;
        foreach (var m in raw.Split(MailSep))
        {
            var f = m.Split(FieldSep);
            if (f.Length < 8) continue;
            int.TryParse(f[5], out int amount);
            list.Add(new Mail { Id = f[0], From = f[1], Subject = f[2], Body = f[3], Reward = f[4], Amount = amount, Read = f[6] == "1", Claimed = f[7] == "1" });
        }
        return list;
    }

    static void SaveMails(List<Mail> mails)
    {
        var parts = new List<string>();
        foreach (var m in mails)
            parts.Add(string.Join(FieldSep.ToString(), new[] { m.Id, m.From, m.Subject, m.Body, m.Reward, m.Amount.ToString(), m.Read ? "1" : "0", m.Claimed ? "1" : "0" }));
        PlayerPrefs.SetString("mails", string.Join(MailSep.ToString(), parts));
        PlayerPrefs.Save();
    }

    static void AddMail(string from, string subject, string body, string reward, int amount)
    {
        var mails = LoadMails();
        mails.Insert(0, new Mail { Id = System.DateTime.Now.Ticks.ToString(), From = from, Subject = subject, Body = body, Reward = reward, Amount = amount });
        SaveMails(mails);
    }

    static int UnreadMails()
    {
        int n = 0;
        foreach (var m in LoadMails()) if (!m.Read || (m.Reward != "" && !m.Claimed)) n++;
        return n;
    }

    // はじめて起動したときのあいさつのメール
    static void EnsureWelcomeMail()
    {
        if (PlayerPrefs.GetInt("welcome_mail", 0) == 1) return;
        PlayerPrefs.SetInt("welcome_mail", 1);
        AddMail("マジカルコンパイル運営", "ようこそ、プログラマーさん！",
            "マジカルコンパイルへようこそ！\nウイルスに困っている人のパソコンを、AI ロボットたちといっしょに助けてあげてください。\n\nはじめてのプレゼントに、経験値を送ります。", "xp", 50);
    }

    static string RewardText(Mail m) =>
        m.Reward == "xp" ? "経験値 +" + m.Amount + "（3人とも）" : m.Reward == "chip" ? "メモリ増設チップ（3人ともメモリ容量 +" + m.Amount + "）" : "";

    // ---- メールボックスの画面 ----

    RectTransform mailLayer, mailList, mailView;
    List<Mail> mails;

    void BuildMailbox()
    {
        mailLayer = MenuScreen("Mailbox", "メールボックス");
        var border = Img(mailLayer, UiKit.RoundRect(28), Purple);
        border.type = Image.Type.Sliced;
        Anchor(border.rectTransform, 0.03f, 0.14f, 0.97f, 0.86f);
        var win = Img(border.rectTransform, UiKit.RoundRect(24), UiKit.Hex("f7eef8"));
        win.type = Image.Type.Sliced;
        Anchor(win.rectTransform, 0, 0, 1, 1, 5, 5, -5, -5);
        var listBg = Img(win.rectTransform, UiKit.RoundRect(18), UiKit.Hex("eab8dd"));
        listBg.type = Image.Type.Sliced;
        Anchor(listBg.rectTransform, 0.015f, 0.03f, 0.36f, 0.97f);
        mailList = NewRect(listBg.rectTransform, "List");
        Anchor(mailList, 0, 0, 1, 1, 12, 12, -12, -12);
        AddVertical(mailList, 8);
        mailList.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;
        mailView = NewRect(win.rectTransform, "View");
        Anchor(mailView, 0.38f, 0.03f, 0.985f, 0.97f);
        var back = CommandButton(mailLayer, "ホームへ", 32, ShowHome, false);
        Anchor((RectTransform)back.transform, 0.03f, 0.03f, 0.2f, 0.11f);
    }

    void ShowMailbox()
    {
        HideScreens();
        mailLayer.gameObject.SetActive(true);
        mails = LoadMails();
        RefreshMailList();
        ClearChildren(mailView);
        if (mails.Count > 0) OpenMail(mails[0]);
        else MakeText(mailView, "メールはありません", 30, Ink, TextAnchor.MiddleCenter, true);
    }

    void RefreshMailList()
    {
        ClearChildren(mailList);
        foreach (var m in mails)
        {
            var mail = m;
            bool news = !m.Read || (m.Reward != "" && !m.Claimed);
            var row = Img(mailList, UiKit.RoundRect(12), news ? Color.white : new Color(1, 1, 1, 0.6f));
            row.type = Image.Type.Sliced;
            row.raycastTarget = true;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 92;
            var b = row.gameObject.AddComponent<Button>();
            b.targetGraphic = row;
            b.onClick.AddListener(() => OpenMail(mail));
            if (news)
            {
                var dot = Img(row.rectTransform, UiKit.Circle(), UiKit.Hex("e8283c")).rectTransform;
                dot.anchorMin = dot.anchorMax = new Vector2(0, 0.5f);
                dot.anchoredPosition = new Vector2(18, 0);
                dot.sizeDelta = new Vector2(14, 14);
            }
            var from = MakeText(row.rectTransform, m.From, 20, UiKit.Hex("6a5a80"), TextAnchor.UpperLeft, true);
            Anchor(from.rectTransform, 0, 0, 1, 1, 34, 8, -10, -8);
            var subj = MakeText(row.rectTransform, (m.Reward != "" ? "［プレゼント］" : "") + m.Subject, 22, Ink, TextAnchor.LowerLeft, true);
            Anchor(subj.rectTransform, 0, 0, 1, 1, 34, 10, -10, -8);
        }
    }

    void OpenMail(Mail m)
    {
        if (!m.Read) { m.Read = true; SaveMails(mails); RefreshMailList(); }
        ClearChildren(mailView);
        var subj = MakeText(mailView, m.Subject, 38, Purple, TextAnchor.UpperLeft, true);
        Anchor(subj.rectTransform, 0, 0.86f, 1, 1, 10, 0, -10, -10);
        var from = MakeText(mailView, "差出人：" + m.From, 24, UiKit.Hex("6a5a80"), TextAnchor.UpperLeft, true);
        Anchor(from.rectTransform, 0, 0.8f, 1, 0.86f, 10, 0, -10, 0);
        var line = Img(mailView, null, UiKit.Hex("d8c8e8")).rectTransform;
        Anchor(line, 0, 0.79f, 1, 0.79f, 10, 0, -10, 3);
        var body = MakeText(mailView, m.Body, 26, Ink, TextAnchor.UpperLeft, false);
        Anchor(body.rectTransform, 0, 0.3f, 1, 0.77f, 10, 0, -10, 0);
        if (m.Reward == "") return;
        var gift = Img(mailView, UiKit.RoundRect(18), m.Claimed ? UiKit.Hex("d8d0e8") : UiKit.Hex("fff4d8"));
        gift.type = Image.Type.Sliced;
        Anchor(gift.rectTransform, 0, 0.04f, 1, 0.27f, 10, 0, -10, 0);
        var box = NewRect(gift.rectTransform, "Box");
        Anchor(box, 0, 0.5f, 0, 0.5f, 20, -50, 120, 50);
        GiftBox(box);
        var gt = MakeText(gift.rectTransform, "プレゼント：" + RewardText(m), 26, Ink, TextAnchor.MiddleLeft, true);
        Anchor(gt.rectTransform, 0, 0, 1, 1, 140, 0, -300, 0);
        if (m.Claimed)
        {
            var done = MakeText(gift.rectTransform, "受け取りずみ", 30, UiKit.Hex("6a6e84"), TextAnchor.MiddleRight, true);
            Anchor(done.rectTransform, 0, 0, 1, 1, 0, 0, -30, 0);
        }
        else
        {
            var take = CommandButton(gift.rectTransform, "受け取る", 32, () => ClaimMail(m), true);
            Anchor((RectTransform)take.transform, 1, 0.5f, 1, 0.5f, -280, -40, -24, 40);
        }
    }

    void ClaimMail(Mail m)
    {
        if (m.Claimed) return;
        m.Claimed = true;
        var up = new List<string>();
        if (m.Reward == "xp") up = GiveXp(m.Amount);
        else if (m.Reward == "chip") { PlayerPrefs.SetInt("chip_mem", MemoryBonus + m.Amount); PlayerPrefs.Save(); }
        SaveMails(mails);
        RefreshMailList();
        OpenMail(m);
        string msg = "受け取った！ " + RewardText(m) + (up.Count > 0 ? "\nレベルアップ：" + string.Join("・", up) : "");
        StartCoroutine(Toast(mailLayer, msg));
    }

    // 画面の上の方に少しだけ出るお知らせ
    IEnumerator Toast(RectTransform parent, string text)
    {
        var t = Img(parent, UiKit.RoundRect(18), new Color(0.1f, 0.07f, 0.18f, 0.92f));
        t.type = Image.Type.Sliced;
        Anchor(t.rectTransform, 0.3f, 0.86f, 0.7f, 0.97f);
        var tx = MakeText(t.rectTransform, text, 26, Color.white, TextAnchor.MiddleCenter, true);
        Fill(tx.rectTransform);
        yield return new WaitForSeconds(2.2f);
        if (t != null) Destroy(t.gameObject);
    }

    // プレゼントの箱（ピンクの箱に黄色いリボン）
    void GiftBox(RectTransform r)
    {
        var body = Img(r, UiKit.RoundRect(8), Pink);
        body.type = Image.Type.Sliced;
        Anchor(body.rectTransform, 0.1f, 0.05f, 0.9f, 0.7f);
        var lid = Img(r, UiKit.RoundRect(8), UiKit.Hex("ff7ac8"));
        lid.type = Image.Type.Sliced;
        Anchor(lid.rectTransform, 0.02f, 0.6f, 0.98f, 0.8f);
        var v = Img(r, null, UiKit.Hex("ffd84a"));
        Anchor(v.rectTransform, 0.44f, 0.05f, 0.56f, 0.8f);
        foreach (var x in new[] { 0.3f, 0.7f })
        {
            var bow = Img(r, UiKit.Circle(), UiKit.Hex("ffd84a")).rectTransform;
            bow.anchorMin = bow.anchorMax = new Vector2(x, 0.88f);
            bow.sizeDelta = new Vector2(34, 24);
        }
    }

    // ================= 依頼完了の通信 =================

    IEnumerator Victory()
    {
        var r = currentRequest;
        bool first = !IsCleared(r);
        int rankBefore = OpenRank();
        PlayerPrefs.SetInt("clear_" + r.Id, 1);
        rankOpened = OpenRank() > rankBefore ? OpenRank() : -1;   // 昇格依頼（またはチュートリアル）で新しいランクが開いた
        gameCleared = r.Final && first;
        // ランク：目標のターン数以内で、だれも倒れなければ S。目標＋2 ターン以内なら A。それより後は B
        bool allAlive = battle.Allies.TrueForAll(a => a.Alive);
        int rank = battle.Turn <= r.ParTurns && allAlive ? 3 : battle.Turn <= r.ParTurns + 2 ? 2 : 1;
        if (rank > BestRank(r)) PlayerPrefs.SetInt("rank_" + r.Id, rank);
        int xp = first ? r.Xp : r.Xp / 2;
        xp = (int)(xp * (rank == 3 ? 1.5f : rank == 2 ? 1.2f : 1f));   // S は 1.5 倍、A は 1.2 倍
        var levelsBefore = LevelMap();
        var xpBefore = new Dictionary<string, int>();
        foreach (var c in Characters.All) xpBefore[c.Art] = GetXp(c.Art);
        GiveXp(xp);
        bool gift = first || Random.value < 0.35f;
        if (gift)
        {
            if (first)
                AddMail(r.Client, "お礼のプレゼント", r.Client + "です。このあいだはパソコンを助けてくれて、本当にありがとう！\nお礼にこれを送ります。使ってください。",
                    r.Stars >= 2 ? "chip" : "xp", r.Stars >= 2 ? 50 : 100);
            else
                AddMail(r.Client, "また助けてくれてありがとう", r.Client + "です。また来てくれてうれしかった！\nちょっとだけど、お礼です。", "xp", 40);
        }
        yield return new WaitForSeconds(0.9f);
        lastRank = rank;
        yield return CompleteCall(r, xp, xpBefore, levelsBefore, gift);
    }

    int lastRank;
    int rankOpened = -1;
    bool gameCleared;

    IEnumerator CompleteCall(Request r, int xp, Dictionary<string, int> xpBefore, Dictionary<string, int> levelsBefore, bool gift)
    {
        var dim = Img(battleLayer, null, new Color(0, 0, 0, 0.6f));
        dim.rectTransform.name = "Call";
        dim.raycastTarget = true;
        Fill(dim.rectTransform);
        var border = Img(dim.rectTransform, UiKit.RoundRect(26), Cyan);
        border.type = Image.Type.Sliced;
        var bw = border.rectTransform;
        Anchor(bw, 0.1f, 0.08f, 0.9f, 0.92f);
        var win = Img(bw, UiKit.RoundRect(22), UiKit.Hex("0c0a22"));
        win.type = Image.Type.Sliced;
        var w = win.rectTransform;
        Anchor(w, 0, 0, 1, 1, 5, 5, -5, -5);
        var bar = Img(w, UiKit.RoundRect(22), UiKit.Hex("1e2a4a"));
        bar.type = Image.Type.Sliced;
        Anchor(bar.rectTransform, 0, 1, 1, 1, 0, -60, 0, 0);
        MakeText(bar.rectTransform, "　通信中 ― " + r.Client + "さん", 30, Cyan, TextAnchor.MiddleLeft, true);
        var live = MakeText(bar.rectTransform, "● LIVE　", 26, UiKit.Hex("ff3b5c"), TextAnchor.MiddleRight, true);
        ambient.StartCoroutine(BlinkText(live));

        // 開くとき：ノイズまじりに広がる
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            bw.localScale = new Vector3(1f, Mathf.Max(0.02f, t), 1f);
            yield return null;
        }
        bw.localScale = Vector3.one;
        StartCoroutine(Noise(0.25f, 0.3f));

        // 左：依頼主の映像（アイコンと音の波）
        var cam = Img(w, UiKit.RoundRect(18), UiKit.Hex("1b1840"));
        cam.type = Image.Type.Sliced;
        Anchor(cam.rectTransform, 0.03f, 0.46f, 0.3f, 0.88f);
        var avatar = Img(cam.rectTransform, UiKit.Circle(), ClientColor(r.Client)).rectTransform;
        avatar.anchorMin = avatar.anchorMax = new Vector2(0.5f, 0.6f);
        avatar.sizeDelta = new Vector2(170, 170);
        MakeText(avatar, r.Client.Substring(0, 1), 90, Color.white, TextAnchor.MiddleCenter, true);
        var cname = MakeText(cam.rectTransform, r.Client + "さん", 28, Color.white, TextAnchor.MiddleCenter, true);
        Anchor(cname.rectTransform, 0, 0.05f, 1, 0.22f);
        var waves = new List<RectTransform>();
        for (int i = 0; i < 7; i++)
        {
            var wv = Img(cam.rectTransform, null, Cyan).rectTransform;
            wv.anchorMin = wv.anchorMax = new Vector2(0.32f + i * 0.06f, 0.26f);
            wv.sizeDelta = new Vector2(8, 10);
            waves.Add(wv);
        }

        // 右：お礼の言葉（1文字ずつ）
        var bubble = Img(w, UiKit.RoundRect(26), Color.white);
        bubble.type = Image.Type.Sliced;
        Anchor(bubble.rectTransform, 0.33f, 0.58f, 0.97f, 0.88f);
        var say = MakeText(bubble.rectTransform, "", 32, Ink, TextAnchor.MiddleLeft, true);
        Anchor(say.rectTransform, 0, 0, 1, 1, 30, 10, -30, -10);
        for (int i = 0; i <= r.Thanks.Length; i++)
        {
            say.text = r.Thanks.Substring(0, i);
            foreach (var wv in waves) wv.sizeDelta = new Vector2(8, Random.Range(8f, 50f));
            yield return new WaitForSeconds(0.035f);
        }
        foreach (var wv in waves) wv.sizeDelta = new Vector2(8, 8);

        // 「依頼完了」のハンコ
        var stamp = Img(w, UiKit.RoundRect(14), new Color(0.85f, 0.1f, 0.2f, 0.92f)).rectTransform;
        stamp.GetComponent<Image>().type = Image.Type.Sliced;
        stamp.anchorMin = stamp.anchorMax = new Vector2(0.78f, 0.5f);
        stamp.sizeDelta = new Vector2(330, 100);
        stamp.localRotation = Quaternion.Euler(0, 0, -10f);
        var st = MakeText(stamp, "依頼完了", 64, Color.white, TextAnchor.MiddleCenter, true);
        AddOutline(st, UiKit.Hex("6a0010"), 3f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f)
        {
            stamp.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, t);
            yield return null;
        }
        stamp.localScale = Vector3.one;
        StartCoroutine(ShakeRect(bw));

        // 経験値
        // ランク（ハンコのとなり）
        yield return new WaitForSeconds(0.2f);
        var rankBox = NewRect(w, "Rank");
        rankBox.anchorMin = rankBox.anchorMax = new Vector2(0.55f, 0.5f);
        RankBadge(rankBox, lastRank, new Vector2(0.5f, 0.5f), Vector2.zero, 2.2f);
        var rankNote = MakeText(w, "ランク　" + battle.Turn + " ターン（S の目標 " + r.ParTurns + " ターン）" + (lastRank == 3 ? "　EXP 1.5 倍！" : lastRank == 2 ? "　EXP 1.2 倍" : ""), 24, Color.white, TextAnchor.MiddleCenter, true);
        Anchor(rankNote.rectTransform, 0.35f, 0.37f, 0.75f, 0.42f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f) { rankBox.localScale = Vector3.one * Mathf.Lerp(2f, 1f, t); yield return null; }
        rankBox.localScale = Vector3.one;

        var xpTitle = MakeText(w, "EXP +" + xp, 52, UiKit.Hex("ffd84a"), TextAnchor.MiddleLeft, true);
        xpTitle.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(xpTitle, Color.black, 3f);
        Anchor(xpTitle.rectTransform, 0.03f, 0.36f, 0.6f, 0.44f);
        var rows = new List<(RobotInfo c, Image fill, Text label, Text up)>();
        for (int i = 0; i < Characters.All.Length; i++)
        {
            var c = Characters.All[i];
            var row = NewRect(w, "Xp");
            Anchor(row, 0.03f + i * 0.32f, 0.12f, 0.33f + i * 0.32f, 0.34f);
            var face = Img(row, UiKit.Art(c.Art + "_idle", FaceCrop), Color.white);
            face.preserveAspect = true;
            Anchor(face.rectTransform, 0, 0.1f, 0.3f, 1);
            var label = MakeText(row, "", 26, Color.white, TextAnchor.UpperLeft, true);
            Anchor(label.rectTransform, 0.32f, 0.5f, 1, 1);
            var back = Img(row, UiKit.RoundRect(8), new Color(1, 1, 1, 0.15f));
            back.type = Image.Type.Sliced;
            Anchor(back.rectTransform, 0.32f, 0.32f, 0.97f, 0.46f);
            var fill = Img(back.rectTransform, UiKit.RoundRect(8), UiKit.Hex("ffd84a"));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            Fill(fill.rectTransform);
            var up = MakeText(row, "", 24, UiKit.Hex("7dff8a"), TextAnchor.UpperLeft, true);
            Anchor(up.rectTransform, 0.32f, 0, 1, 0.3f);
            rows.Add((c, fill, label, up));
        }
        // バーがたまっていく
        var leveled = new HashSet<string>();
        for (float t = 0f; t <= 1f; t += Time.deltaTime / 1.2f)
        {
            foreach (var row in rows)
            {
                int now = xpBefore[row.c.Art] + (int)(xp * Mathf.Clamp01(t));
                int lv = Levels.LevelOf(now, out int into, out int need);
                row.label.text = row.c.Name + "　Lv." + lv;
                row.fill.fillAmount = (float)into / need;
                if (lv > levelsBefore[row.c.Art] && !leveled.Contains(row.c.Art))
                {
                    leveled.Add(row.c.Art);
                    row.up.text = "LEVEL UP!　メモリ +" + Levels.MemoryPerLevel + "・HP +" + Levels.HpPerLevel;
                    var pop = MakeText(row.up.transform.parent, "LEVEL UP!", 44, UiKit.Hex("7dff8a"), TextAnchor.MiddleCenter, true);
                    pop.fontStyle = FontStyle.BoldAndItalic;
                    AddOutline(pop, Color.black, 3f);
                    var prt = pop.rectTransform;
                    prt.anchorMin = prt.anchorMax = new Vector2(0.6f, 0.9f);
                    prt.sizeDelta = new Vector2(320, 60);
                    StartCoroutine(Popup(prt, pop));
                }
            }
            yield return null;
        }

        if (gift)
        {
            var g = MakeText(w, "メールボックスに " + r.Client + "さんからメールが届きました", 24, Pink, TextAnchor.MiddleLeft, true);
            Anchor(g.rectTransform, 0.03f, 0.04f, 0.6f, 0.11f);
        }

        // ランクが開いた・ゲームクリア
        if (rankOpened >= 0 || gameCleared)
        {
            yield return new WaitForSeconds(0.3f);
            string big = gameCleared ? "GAME CLEAR!" : "RANK UP!";
            string small = gameCleared ? "マザーウイルスを倒した！ 街のパソコンは救われた"
                : rankOpened >= Requests.MaxRank ? "最終依頼が受けられるようになった！"
                : Requests.RankName(rankOpened) + "の依頼が受けられるようになった！";
            var band = Img(w, UiKit.Slant(), gameCleared ? Magenta : UiKit.Hex("ff8a00"));
            band.type = Image.Type.Sliced;
            Anchor(band.rectTransform, 0.2f, 0.64f, 0.8f, 0.8f);
            var bt = MakeText(band.rectTransform, big + "\n<size=26>" + small + "</size>", 54, Color.white, TextAnchor.MiddleCenter, true);
            bt.supportRichText = true;
            bt.fontStyle = FontStyle.BoldAndItalic;
            AddOutline(bt, Color.black, 3f);
            var brt = band.rectTransform;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f) { brt.localScale = new Vector3(1f, Mathf.Lerp(0f, 1f, t), 1f); yield return null; }
            brt.localScale = Vector3.one;
            StartCoroutine(ShakeRect(bw));
        }
        var home = CommandButton(w, "ホームへ", 32, ShowHome, false);
        Anchor((RectTransform)home.transform, 0.62f, 0.03f, 0.78f, 0.11f);
        var list = CommandButton(w, "依頼一覧へ", 32, ShowRequests, true);
        Anchor((RectTransform)list.transform, 0.8f, 0.03f, 0.97f, 0.11f);
    }

    IEnumerator ShakeRect(RectTransform rt)
    {
        var start = rt.anchoredPosition;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            if (rt == null) yield break;
            rt.anchoredPosition = start + Random.insideUnitCircle * 10f * (1f - t);
            yield return null;
        }
        if (rt != null) rt.anchoredPosition = start;
    }

    // 依頼主のアイコンの色（名前から決める）
    static Color ClientColor(string name)
    {
        var colors = new[] { "ff7aa8", "4fb8ff", "ffb32a", "7c5cc9", "2fae4a", "e0408a" };
        int h = 0;
        foreach (var ch in name) h = h * 31 + ch;
        return UiKit.Hex(colors[Mathf.Abs(h) % colors.Length]);
    }
}
