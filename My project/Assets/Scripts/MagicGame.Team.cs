// チーム編成と装備
//   チーム編成：出撃する 3 体を選ぶ。各キャラの「装備」から装備画面へ
//   装備：      真ん中にキャラ、左に装備の枠（レベルで増える）、右に装備できるもの。
//               追加ブロック（もし〜なら・〜回繰り返す）をはめると、そのキャラのプログラム画面で使える。
//               強化チップ（メモリ圧縮・クロックアップ・防御プログラム）は能力が上がる
//               枠と真ん中のキャラを細い線でつなぐ、暗い画面に光る線の見た目
// 保存は PlayerPrefs（party, mods_キャラ）

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MagicBattle;

public partial class MagicGame
{
    static readonly Color HudCyan = UiKit.Hex("3ff0ff");
    static readonly Color HudRed = UiKit.Hex("ff3b5c");
    static readonly Color HudPanel = new Color(0.06f, 0.04f, 0.1f, 0.92f);

    RectTransform teamLayer, partyRow, rosterRow, loadoutLayer, loadoutArea;
    Text teamMessage;
    string loadoutArt;

    // ================= 保存 =================

    static List<string> LoadParty()
    {
        var list = new List<string>();
        foreach (var a in PlayerPrefs.GetString("party", "").Split(','))
            if (a != "" && System.Array.Exists(Characters.All, c => c.Art == a)) list.Add(a);
        if (list.Count == 0) for (int i = 0; i < Characters.All.Length && i < 3; i++) list.Add(Characters.All[i].Art);
        return list;
    }

    static void SaveParty(List<string> party)
    {
        PlayerPrefs.SetString("party", string.Join(",", party));
        PlayerPrefs.Save();
    }

    // 装備の枠（空きは ""）。枠の数はレベルで決まる
    static string[] LoadMods(string art)
    {
        int slots = Modules.Slots(Levels.LevelOf(GetXp(art)));
        var saved = PlayerPrefs.GetString("mods_" + art, "").Split(',');
        var mods = new string[slots];
        for (int i = 0; i < slots; i++) mods[i] = i < saved.Length && Modules.Find(saved[i]) != null ? saved[i] : "";
        return mods;
    }

    static void SaveMods(string art, string[] mods)
    {
        PlayerPrefs.SetString("mods_" + art, string.Join(",", mods));
        PlayerPrefs.Save();
    }

    static Dictionary<string, string[]> LoadoutMap()
    {
        var d = new Dictionary<string, string[]>();
        foreach (var c in Characters.All) d[c.Art] = LoadMods(c.Art);
        return d;
    }

    static RobotInfo Robot(string art) => System.Array.Find(Characters.All, c => c.Art == art);

    // ================= 暗い HUD の背景 =================

    RectTransform HudScreen(string name, string heading)
    {
        var layer = Layer(name);
        var bg = layer.gameObject.AddComponent<RawImage>();
        bg.texture = UiKit.VerticalGradient(UiKit.Hex("05030a"), UiKit.Hex("1c0a26"));
        var grid = NewRect(layer, "Grid").gameObject.AddComponent<RawImage>();
        Fill(grid.rectTransform);
        grid.texture = CircuitTexture();
        grid.uvRect = new Rect(0, 0, 1920f / 96f, 1080f / 96f);
        grid.color = new Color(1f, 0.4f, 0.6f, 0.18f);
        grid.raycastTarget = false;
        var header = Img(layer, UiKit.Slant(), HudRed);
        header.type = Image.Type.Sliced;
        Anchor(header.rectTransform, -0.02f, 0.89f, 0.3f, 0.97f);
        var ht = MakeText(header.rectTransform, heading, 52, Color.white, TextAnchor.MiddleCenter, true);
        ht.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(ht, UiKit.Hex("4a0010"), 3f);
        var lineTop = Img(layer, null, new Color(HudCyan.r, HudCyan.g, HudCyan.b, 0.6f)).rectTransform;
        Anchor(lineTop, 0.3f, 0.93f, 0.98f, 0.93f, 0, -1, 0, 1);
        layer.gameObject.SetActive(false);
        return layer;
    }

    // 光るふちの箱（HUD の部品）
    RectTransform HudBox(Transform parent, Color edge)
    {
        var border = Img(parent, UiKit.RoundRect(6), new Color(edge.r, edge.g, edge.b, 0.85f));
        border.type = Image.Type.Sliced;
        var inner = Img(border.rectTransform, UiKit.RoundRect(5), HudPanel);
        inner.type = Image.Type.Sliced;
        Anchor(inner.rectTransform, 0, 0, 1, 1, 2, 2, -2, -2);
        var accent = Img(border.rectTransform, null, edge).rectTransform;   // 左の太い線
        Anchor(accent, 0, 0, 0, 1, 0, 6, 6, -6);
        return border.rectTransform;
    }

    // ================= チーム編成 =================

    void BuildTeam()
    {
        teamLayer = HudScreen("Team", "チーム編成");
        var pl = MakeText(teamLayer, "出撃メンバー", 30, HudCyan, TextAnchor.MiddleLeft, true);
        Anchor(pl.rectTransform, 0.04f, 0.82f, 0.5f, 0.88f);
        partyRow = NewRect(teamLayer, "Party");
        Anchor(partyRow, 0.04f, 0.4f, 0.96f, 0.82f);
        var rl = MakeText(teamLayer, "ロボット一覧（クリックで出撃メンバーに入れる／外す）", 26, HudCyan, TextAnchor.MiddleLeft, true);
        Anchor(rl.rectTransform, 0.04f, 0.33f, 0.9f, 0.39f);
        rosterRow = NewRect(teamLayer, "Roster");
        Anchor(rosterRow, 0.04f, 0.13f, 0.96f, 0.33f);
        teamMessage = MakeText(teamLayer, "", 26, HudRed, TextAnchor.MiddleRight, true);
        Anchor(teamMessage.rectTransform, 0.3f, 0.03f, 0.96f, 0.11f);
        var back = CommandButton(teamLayer, "ホームへ", 32, ShowHome, false);
        Anchor((RectTransform)back.transform, 0.03f, 0.03f, 0.2f, 0.11f);
    }

    void ShowTeam()
    {
        HideScreens();
        teamLayer.gameObject.SetActive(true);
        RefreshTeam();
    }

    void RefreshTeam()
    {
        var party = LoadParty();
        ClearChildren(partyRow);
        for (int i = 0; i < 3; i++)
        {
            var box = HudBox(partyRow, i < party.Count ? HudCyan : new Color(0.4f, 0.4f, 0.5f));
            Anchor(box, i / 3f + 0.008f, 0, (i + 1) / 3f - 0.008f, 1);
            var num = MakeText(box, "0" + (i + 1), 44, new Color(1, 1, 1, 0.25f), TextAnchor.UpperLeft, true);
            num.fontStyle = FontStyle.BoldAndItalic;
            Anchor(num.rectTransform, 0, 0, 1, 1, 20, 0, 0, -8);
            if (i >= party.Count)
            {
                MakeText(box, "空き", 34, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleCenter, true);
                continue;
            }
            var c = Robot(party[i]);
            var glow = Img(box, UiKit.Glow(), new Color(1f, 0.3f, 0.6f, 0.35f)).rectTransform;
            Anchor(glow, 0.05f, 0.05f, 0.55f, 0.95f);
            var art = Img(box, UiKit.Art(c.Art + "_idle"), Color.white);
            art.preserveAspect = true;
            Anchor(art.rectTransform, 0.05f, 0.08f, 0.55f, 0.92f);
            int lv = Levels.LevelOf(GetXp(c.Art));
            var name = MakeText(box, c.Name + "　<size=26>Lv." + lv + "</size>", 38, Color.white, TextAnchor.UpperLeft, true);
            name.supportRichText = true;
            Anchor(name.rectTransform, 0.55f, 0.72f, 1, 0.95f, 0, 0, -10, 0);
            var mods = LoadMods(c.Art);
            var sb = new System.Text.StringBuilder();
            foreach (var m in mods) sb.Append(m == "" ? "<color=#6a6e84>― 空き ―</color>" : "<color=#3ff0ff>" + Modules.Find(m).Name + "</color>").Append('\n');
            var eq = MakeText(box, sb.ToString(), 22, Color.white, TextAnchor.UpperLeft, false);
            eq.supportRichText = true;
            Anchor(eq.rectTransform, 0.55f, 0.25f, 1, 0.72f, 0, 0, -10, 0);
            string art2 = c.Art;
            var btn = CommandButton(box, "装備", 30, () => ShowLoadout(art2), true);
            Anchor((RectTransform)btn.transform, 0.56f, 0.06f, 0.96f, 0.22f);
        }

        ClearChildren(rosterRow);
        int slots = 5;
        for (int i = 0; i < slots; i++)
        {
            bool real = i < Characters.All.Length;
            bool inParty = real && party.Contains(Characters.All[i].Art);
            var box = HudBox(rosterRow, inParty ? HudRed : new Color(0.4f, 0.4f, 0.5f));
            Anchor(box, i / (float)slots + 0.005f, 0, (i + 1) / (float)slots - 0.005f, 1);
            if (!real)
            {
                var sil = Img(box, UiKit.Art("red_idle", FaceCrop), new Color(0.15f, 0.13f, 0.22f));
                sil.preserveAspect = true;
                Anchor(sil.rectTransform, 0.04f, 0.1f, 0.42f, 0.9f);
                var q = MakeText(box, "？？？\n<size=20>まだ仲間になっていない</size>", 30, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleLeft, true);
                q.supportRichText = true;
                Anchor(q.rectTransform, 0.44f, 0, 1, 1);
                continue;
            }
            var c = Characters.All[i];
            var face = Img(box, UiKit.Art(c.Art + "_idle", FaceCrop), Color.white);
            face.preserveAspect = true;
            Anchor(face.rectTransform, 0.04f, 0.1f, 0.42f, 0.9f);
            var t = MakeText(box, c.Name + "\n<size=20>" + TraitText(c) + "</size>" + (inParty ? "\n<size=20><color=#ff3b5c>出撃中</color></size>" : ""), 30, Color.white, TextAnchor.MiddleLeft, true);
            t.supportRichText = true;
            Anchor(t.rectTransform, 0.44f, 0, 1, 1);
            var b = box.gameObject.AddComponent<Button>();
            b.targetGraphic = box.GetComponent<Image>();
            box.GetComponent<Image>().raycastTarget = true;
            string art = c.Art;
            b.onClick.AddListener(() => ToggleParty(art));
        }
        int need = Mathf.Min(3, Characters.All.Length);
        teamMessage.text = party.Count < need ? "出撃メンバーは " + need + " 人にしてください" : "";
    }

    void ToggleParty(string art)
    {
        var party = LoadParty();
        if (party.Contains(art)) party.Remove(art);
        else if (party.Count < 3) party.Add(art);
        else { teamMessage.text = "出撃メンバーは 3 人までです（だれかを外してから）"; return; }
        SaveParty(party);
        RefreshTeam();
    }

    // ================= 装備 =================

    void BuildLoadout()
    {
        loadoutLayer = HudScreen("Loadout", "装備");
        loadoutArea = NewRect(loadoutLayer, "Area");
        Fill(loadoutArea);
        var back = CommandButton(loadoutLayer, "チーム編成へ", 30, ShowTeam, false);
        Anchor((RectTransform)back.transform, 0.03f, 0.03f, 0.22f, 0.11f);
        var hint = MakeText(loadoutLayer, "右の一覧をクリックで空いている枠にはめる。左の枠をクリックで外す。枠はレベル 3・5 で増える", 22, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleLeft, false);
        Anchor(hint.rectTransform, 0.24f, 0.03f, 0.98f, 0.11f);
    }

    void ShowLoadout(string art)
    {
        loadoutArt = art;
        HideScreens();
        loadoutLayer.gameObject.SetActive(true);
        RefreshLoadout();
    }

    void RefreshLoadout()
    {
        ClearChildren(loadoutArea);
        var c = Robot(loadoutArt);
        var mods = LoadMods(c.Art);
        int lv = Levels.LevelOf(GetXp(c.Art), out int into, out int need);

        // 真ん中：キャラと、後ろの光と魔法陣
        var glow = Img(loadoutArea, UiKit.Glow(), new Color(1f, 0.25f, 0.45f, 0.45f)).rectTransform;
        glow.anchorMin = glow.anchorMax = new Vector2(0.5f, 0.5f);
        glow.sizeDelta = new Vector2(760, 900);
        var ring = Img(loadoutArea, UiKit.MagicCircle(), new Color(HudCyan.r, HudCyan.g, HudCyan.b, 0.25f)).rectTransform;
        ring.anchorMin = ring.anchorMax = new Vector2(0.5f, 0.48f);
        ring.sizeDelta = new Vector2(640, 640);
        ambient.StartCoroutine(SpinForever(ring, 8f));
        var art = Img(loadoutArea, UiKit.Art(c.Art + "_idle"), Color.white).rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.14f);
        art.pivot = new Vector2(0.5f, 0f);
        art.sizeDelta = UiKit.ArtSize(c.Art + "_idle") * 10f;

        // キャラの能力
        int hp = c.Hp + (lv - 1) * Levels.HpPerLevel;
        int mem = c.Capacity + (lv - 1) * Levels.MemoryPerLevel + MemoryBonus + (System.Array.IndexOf(mods, Modules.Memory) >= 0 ? 200 : 0);
        var info = MakeText(loadoutArea,
            c.Name + "　<size=30>Lv." + lv + "</size>\n<size=24><color=#3ff0ff>HP " + hp + "　メモリ " + mem
            + (System.Array.IndexOf(mods, Modules.Clock) >= 0 ? "　速さ +15%" : "")
            + (System.Array.IndexOf(mods, Modules.Guard) >= 0 ? "　ダメージ -20%" : "") + "</color>\n" + TraitText(c) + "</size>",
            48, Color.white, TextAnchor.UpperCenter, true);
        info.supportRichText = true;
        Anchor(info.rectTransform, 0.3f, 0.74f, 0.7f, 0.88f);
        var xpBack = Img(loadoutArea, null, new Color(1, 1, 1, 0.12f)).rectTransform;
        Anchor(xpBack, 0.4f, 0.735f, 0.6f, 0.735f, 0, -3, 0, 3);
        var xpFill = Img(xpBack, null, UiKit.Hex("ffd84a"));
        xpFill.type = Image.Type.Filled;
        xpFill.fillMethod = Image.FillMethod.Horizontal;
        xpFill.sprite = UiKit.RoundRect(2);
        xpFill.fillAmount = (float)into / need;
        Fill(xpFill.rectTransform);

        // 左：装備の枠（4 つぶん並べ、まだの枠は鍵）
        var slotTitle = MakeText(loadoutArea, "装備の枠", 28, HudRed, TextAnchor.MiddleLeft, true);
        Anchor(slotTitle.rectTransform, 0.04f, 0.8f, 0.3f, 0.86f);
        for (int i = 0; i < 4; i++)
        {
            float top = 0.79f - i * 0.16f;
            bool open = i < mods.Length;
            string id = open ? mods[i] : "";
            var box = HudBox(loadoutArea, !open ? new Color(0.3f, 0.3f, 0.36f) : id == "" ? new Color(0.5f, 0.5f, 0.6f) : HudRed);
            Anchor(box, 0.04f, top - 0.14f, 0.3f, top);
            ConnectLine(box, new Vector2(0.3f, top - 0.07f), new Vector2(0.44f, 0.55f - i * 0.04f), open && id != "" ? HudRed : new Color(1, 1, 1, 0.15f));
            if (!open)
            {
                MakeText(box, "　鍵つき　Lv." + (i == 2 ? 3 : 5) + " で解放", 24, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleLeft, true);
                continue;
            }
            if (id == "")
            {
                MakeText(box, "　― 空き ―", 26, new Color(1, 1, 1, 0.45f), TextAnchor.MiddleLeft, true);
                continue;
            }
            var m = Modules.Find(id);
            ModuleIcon(box, m);
            var t = MakeText(box, m.Name + "\n<size=18><color=#c8c0e0>" + m.Description + "</color></size>", 26, Color.white, TextAnchor.MiddleLeft, true);
            t.supportRichText = true;
            Anchor(t.rectTransform, 0, 0, 1, 1, 96, 4, -10, -4);
            var b = box.gameObject.AddComponent<Button>();
            box.GetComponent<Image>().raycastTarget = true;
            b.targetGraphic = box.GetComponent<Image>();
            int slot = i;
            b.onClick.AddListener(() => { mods[slot] = ""; SaveMods(c.Art, mods); RefreshLoadout(); });
        }

        // 右：装備できるもの
        var listTitle = MakeText(loadoutArea, "装備できるもの", 28, HudCyan, TextAnchor.MiddleLeft, true);
        Anchor(listTitle.rectTransform, 0.7f, 0.8f, 0.96f, 0.86f);
        for (int i = 0; i < Modules.All.Length; i++)
        {
            var m = Modules.All[i];
            bool equipped = System.Array.IndexOf(mods, m.Id) >= 0;
            float top = 0.79f - i * 0.13f;
            var box = HudBox(loadoutArea, equipped ? new Color(0.3f, 0.3f, 0.36f) : HudCyan);
            Anchor(box, 0.7f, top - 0.115f, 0.96f, top);
            ConnectLine(box, new Vector2(0.7f, top - 0.06f), new Vector2(0.56f, 0.6f - i * 0.04f), equipped ? new Color(1, 1, 1, 0.1f) : new Color(HudCyan.r, HudCyan.g, HudCyan.b, 0.35f));
            ModuleIcon(box, m);
            var t = MakeText(box, m.Name + "　<size=18>" + (m.IsBlock ? "追加ブロック" : "強化チップ") + (equipped ? "・装備中" : "") + "</size>\n<size=18><color=#c8c0e0>" + m.Description + "</color></size>",
                24, equipped ? new Color(1, 1, 1, 0.45f) : Color.white, TextAnchor.MiddleLeft, true);
            t.supportRichText = true;
            Anchor(t.rectTransform, 0, 0, 1, 1, 96, 4, -10, -4);
            if (equipped) continue;
            var b = box.gameObject.AddComponent<Button>();
            box.GetComponent<Image>().raycastTarget = true;
            b.targetGraphic = box.GetComponent<Image>();
            string id = m.Id;
            b.onClick.AddListener(() => Equip(c.Art, id));
        }
    }

    void Equip(string art, string id)
    {
        var mods = LoadMods(art);
        int empty = System.Array.IndexOf(mods, "");
        if (empty < 0)
        {
            StartCoroutine(Toast(loadoutLayer, "枠がいっぱいです。左の枠をクリックして外してください"));
            return;
        }
        mods[empty] = id;
        SaveMods(art, mods);
        RefreshLoadout();
    }

    // 部品の小さなアイコン（色つきの四角に短い文字）
    void ModuleIcon(RectTransform box, Module m)
    {
        var col = m.Id == Modules.If ? UiKit.Hex("ffab19") : m.Id == Modules.Repeat ? UiKit.Hex("f0801e")
                : m.Id == Modules.Memory ? UiKit.Hex("4a8fe0") : m.Id == Modules.Clock ? UiKit.Hex("2fae4a") : UiKit.Hex("7c5cc9");
        var icon = Img(box, UiKit.RoundRect(8), col);
        icon.type = Image.Type.Sliced;
        Anchor(icon.rectTransform, 0, 0.5f, 0, 0.5f, 18, -32, 82, 32);
        string mark = m.Id == Modules.If ? "もし" : m.Id == Modules.Repeat ? "くり\nかえし" : m.Id == Modules.Memory ? "MEM" : m.Id == Modules.Clock ? "CLK" : "DEF";
        MakeText(icon.rectTransform, mark, mark.Length > 3 ? 16 : 20, Color.white, TextAnchor.MiddleCenter, true);
    }

    // 2 点を細い光る線でつなぐ（画面の割合で指定）
    void ConnectLine(RectTransform owner, Vector2 a, Vector2 b, Color color)
    {
        var line = Img(loadoutArea, null, color).rectTransform;
        line.SetAsFirstSibling();
        var size = loadoutArea.rect.size;
        Vector2 pa = new Vector2(a.x * size.x, a.y * size.y), pb = new Vector2(b.x * size.x, b.y * size.y);
        line.anchorMin = line.anchorMax = Vector2.zero;
        line.anchoredPosition = (pa + pb) / 2f;
        line.sizeDelta = new Vector2(Vector2.Distance(pa, pb), 2f);
        line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);
        var dot = Img(loadoutArea, UiKit.Circle(), color).rectTransform;
        dot.anchorMin = dot.anchorMax = Vector2.zero;
        dot.anchoredPosition = pb;
        dot.sizeDelta = new Vector2(10, 10);
    }
}
