// 魔法プログラムバトルのルール（Unity に依存しない C#）
// ブロックの種類・メモリ・処理速度・ダメージの計算と、タイムラインで進む戦闘の進め方。
// 数値は仮。docs/決めたこと.md の「ゲームのルール」と合わせる

using System;
using System.Collections.Generic;

namespace MagicBattle
{
    // ブロックの種類。この順番で並べないと送信できない（追加効果は無くてもよい）
    // MagicLink（マジカルリンク）はリンクプログラムだけで使える、最後に置くブロック
    public enum BlockKind { Element, Target, Power, Shots, Effect, MagicLink }

    public enum Element { Fire, Ice, None }
    public enum TargetType { One, All }
    public enum Effect { Blast, Heal, Slow }
    public enum Trait { FireUp, IceUp, MemoryUp }

    // 並べたブロック 1 つ。Value は選んでいる選択肢の番号
    public class Block
    {
        public BlockKind Kind;
        public int Value;
        public Block(BlockKind kind, int value = 0) { Kind = kind; Value = value; }
        public Block Clone() => new Block(Kind, Value);
    }

    public static class Blocks
    {
        public static readonly BlockKind[] Order =
            { BlockKind.Element, BlockKind.Target, BlockKind.Power, BlockKind.Shots, BlockKind.Effect };
        public static readonly BlockKind[] LinkOrder =
            { BlockKind.Element, BlockKind.Target, BlockKind.Power, BlockKind.Shots, BlockKind.Effect, BlockKind.MagicLink };

        // 威力の 200・400 はリンクプログラムだけ（威力の上限が上がる）
        public static readonly int[] PowerValues = { 10, 30, 50, 80, 120, 200, 400 };
        public const int NormalPowerCount = 5;
        public static readonly int[] ShotValues = { 1, 2, 3 };

        public static int OptionCount(BlockKind k, bool link = false)
        {
            switch (k)
            {
                case BlockKind.Element: return 3;
                case BlockKind.Target: return 2;
                case BlockKind.Power: return link ? PowerValues.Length : NormalPowerCount;
                case BlockKind.Shots: return ShotValues.Length;
                case BlockKind.MagicLink: return 1;
                default: return 3;
            }
        }

        // 選択肢の文字（ブロックの白い枠の中）
        public static string Label(BlockKind k, int v)
        {
            switch (k)
            {
                case BlockKind.Element: return new[] { "炎", "氷", "無" }[v];
                case BlockKind.Target: return new[] { "1体", "全体" }[v];
                case BlockKind.Power: return PowerValues[v].ToString();
                case BlockKind.Shots: return ShotValues[v].ToString();
                case BlockKind.MagicLink: return "マジカルリンク";
                default: return new[] { "爆破", "回復", "スロー" }[v];
            }
        }

        // ブロックの右側の文字
        public static string Suffix(BlockKind k)
        {
            switch (k)
            {
                case BlockKind.Element: return "属性を選択";
                case BlockKind.Target: return "に攻撃";
                case BlockKind.Power: return "の威力";
                case BlockKind.Shots: return "回打つ";
                case BlockKind.MagicLink: return "でコンビ魔法";
                default: return "を得る";
            }
        }

        public static string KindName(BlockKind k)
        {
            switch (k)
            {
                case BlockKind.Element: return "属性";
                case BlockKind.Target: return "対象";
                case BlockKind.Power: return "威力";
                case BlockKind.Shots: return "発射方法";
                case BlockKind.MagicLink: return "マジカルリンク";
                default: return "追加効果";
            }
        }

        // ブロックが使うメモリ（マジカルリンクは「残り全部」なので 0 を返し、Compiler で数える）
        public static int Memory(BlockKind k, int v)
        {
            switch (k)
            {
                case BlockKind.MagicLink: return 0;
                case BlockKind.Element: return v == 2 ? 50 : 100;
                case BlockKind.Target: return v == 0 ? 100 : 300;
                case BlockKind.Power: return PowerValues[v] * 3;
                case BlockKind.Shots: return new[] { 50, 200, 400 }[v];
                default: return v == 2 ? 200 : 150;
            }
        }
    }

    // 送信が通った魔法
    public class Spell
    {
        public Element Element;
        public bool HasElement2;      // リンクプログラムでは属性を 2 つ選べる
        public Element Element2;
        public float LinkBonus = 1f;  // マジカルリンクの威力の倍率
        public bool IsLink;
        public Unit Partner;          // リンクの相手
        public TargetType Target;
        public int Power;
        public int Shots;
        public bool HasEffect;
        public Effect Effect;
        public int Memory;

        public string Describe()
        {
            string s = Blocks.Label(BlockKind.Element, (int)Element) + (HasElement2 ? "＋" + Blocks.Label(BlockKind.Element, (int)Element2) : "")
                     + "・" + Blocks.Label(BlockKind.Target, (int)Target) + "・威力" + Power + "×" + Shots + "回";
            if (HasEffect) s += "・" + Blocks.Label(BlockKind.Effect, (int)Effect);
            if (LinkBonus > 1f) s += "・マジカルリンク×" + LinkBonus.ToString("0.0");
            return s;
        }

        public bool Has(Element e) => Element == e || (HasElement2 && Element2 == e);
    }

    public static class Compiler
    {
        // マジカルリンク以外のブロックが使うメモリ
        static int BaseMemory(List<Block> code)
        {
            int m = 0;
            foreach (var b in code) m += Blocks.Memory(b.Kind, b.Value);
            return m;
        }

        // 使うメモリ。マジカルリンクがあれば、メモリ容量を全部使う
        public static int MemoryUsed(List<Block> code, int capacity)
        {
            int m = BaseMemory(code);
            return code.Exists(b => b.Kind == BlockKind.MagicLink) ? Math.Max(m, capacity) : m;
        }

        // マジカルリンクに回せるメモリ（残り全部）
        public static int MagicLinkMemory(List<Block> code, int capacity) => Math.Max(0, capacity - BaseMemory(code));

        // マジカルリンクの威力の倍率：残りのメモリ 2000 ごとに +1（最大 2 倍）
        public static float LinkBonusFor(int memory) => Math.Min(2f, 1f + memory / 2000f);

        // 送信できるか確かめる。できないときは理由を message に入れて false。link = リンクプログラム
        public static bool Check(List<Block> code, int capacity, out string message, bool link = false)
        {
            if (code.Count == 0) { message = "ブロックが1つもありません"; return false; }
            int elements = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var b = code[i];
                if (b.Kind == BlockKind.Element) elements++;
                if (b.Kind == BlockKind.MagicLink && !link) { message = "マジカルリンクはリンクプログラムだけで使えます"; return false; }
                if (b.Kind == BlockKind.Power && b.Value >= Blocks.NormalPowerCount && !link) { message = "威力200以上はリンクプログラムだけで使えます"; return false; }
                if (i == 0) continue;
                var prev = code[i - 1];
                if (b.Kind == prev.Kind && !(link && b.Kind == BlockKind.Element))
                {
                    message = Blocks.KindName(b.Kind) + "のブロックが2つあります";
                    return false;
                }
                if (b.Kind < prev.Kind)
                {
                    message = "順番が違います（" + Blocks.KindName(b.Kind) + "は" + Blocks.KindName(prev.Kind) + "より前）";
                    return false;
                }
            }
            if (elements > 2) { message = "属性のブロックは2つまでです"; return false; }
            for (var k = BlockKind.Element; k <= BlockKind.Shots; k++)
            {
                if (!code.Exists(b => b.Kind == k)) { message = Blocks.KindName(k) + "のブロックがありません"; return false; }
            }
            int used = BaseMemory(code);
            if (used > capacity) { message = "メモリオーバー（" + used + " / " + capacity + "）"; return false; }
            if (code.Exists(b => b.Kind == BlockKind.MagicLink) && capacity - used < 100)
            {
                message = "マジカルリンクに使うメモリが足りません（残り100以上いる）";
                return false;
            }
            message = "送信できます";
            return true;
        }

        // Check が通った code から魔法を作る
        public static Spell Build(List<Block> code, int capacity)
        {
            var s = new Spell { Memory = MemoryUsed(code, capacity) };
            bool firstElement = true;
            foreach (var b in code)
            {
                switch (b.Kind)
                {
                    case BlockKind.Element:
                        if (firstElement) { s.Element = (Element)b.Value; firstElement = false; }
                        else { s.HasElement2 = true; s.Element2 = (Element)b.Value; }
                        break;
                    case BlockKind.Target: s.Target = (TargetType)b.Value; break;
                    case BlockKind.Power: s.Power = Blocks.PowerValues[b.Value]; break;
                    case BlockKind.Shots: s.Shots = Blocks.ShotValues[b.Value]; break;
                    case BlockKind.Effect: s.HasEffect = true; s.Effect = (Effect)b.Value; break;
                    case BlockKind.MagicLink: s.LinkBonus = LinkBonusFor(MagicLinkMemory(code, capacity)); break;
                }
            }
            return s;
        }

        // メモリを使うほど遅くなる。タイムラインを 1 秒に進む量
        public static float Speed(int used, int capacity)
        {
            float r = capacity > 0 ? (float)used / capacity : 1f;
            return 45f * (1.5f - Math.Min(1f, r));
        }

        public static string SpeedLabel(int used, int capacity)
        {
            float r = capacity > 0 ? (float)used / capacity : 1f;
            return r < 0.4f ? "速い" : r < 0.7f ? "ふつう" : "遅い";
        }
    }

    // 味方（AI ロボット）と敵
    public class Unit
    {
        public string Name;
        public string Short;      // タイムラインのアイコンの 1 文字
        public string Art;        // ドット絵の名前（ArtData.cs。味方は「名前_ポーズ」）
        public bool IsAlly;
        public int MaxHp, Hp;
        public float Gauge;       // タイムライン上の位置。Battle.GaugeLength で行動する
        public float Speed;
        public bool ActedThisTurn;

        // 味方だけ
        public Trait Trait;
        public int Capacity;      // もとのメモリ容量（チャージの分は Battle.CapacityOf で足す）
        public Spell Spell;       // 送信した魔法。null なら何もしない
        public bool Charging;     // このターンはチャージする（魔法を打たない）
        public bool Charged;      // チャージした次のターン：メモリ容量と攻撃力が上がる
        public Unit LinkPartner;  // リンクプログラムで組んだ相手（魔法を打つ側が持つ）
        public Unit LinkedTo;     // リンクの相手に選ばれた（このターンは魔法を打たない）

        // 敵だけ
        public bool HasWeakness;
        public Element Weakness;
        public int Attack;

        public bool Alive => Hp > 0;

        public string TraitName()
        {
            switch (Trait)
            {
                case Trait.FireUp: return "炎属性 火力UP";
                case Trait.IceUp: return "氷属性 火力UP";
                default: return "メモリ容量UP";
            }
        }
    }

    public class Hit
    {
        public Unit Target;
        public int Amount;
        public bool Heal;
    }

    public class ActionResult
    {
        public Unit Actor;
        public readonly List<Hit> Hits = new List<Hit>();
        public string Text;
    }

    public class Battle
    {
        public const float GaugeLength = 100f;
        public const float NoProgramSpeed = 40f;

        public readonly List<Unit> Allies = new List<Unit>();
        public readonly List<Unit> Enemies = new List<Unit>();
        public int Turn = 1;
        public int TargetIndex;   // 「1体」の魔法でねらう敵
        public int LinksLeft = 2; // リンクプログラムを使える残りの回数（1試合）
        public const float ChargeMemory = 1.3f, ChargePower = 1.3f;
        readonly Random rng;

        public Battle(int seed)
        {
            rng = new Random(seed);
            Allies.Add(Ally("レッド", "レ", "red", Trait.FireUp, 1000));
            Allies.Add(Ally("ブルー", "ブ", "blue", Trait.IceUp, 1000));
            Allies.Add(Ally("イエロー", "イ", "yellow", Trait.MemoryUp, 1500));
            // 敵はパソコンに入りこんだウイルス
            Enemies.Add(Enemy("バグ", "バ", "bug", 400, 35, 30f, true, Element.Fire));
            Enemies.Add(Enemy("ワーム", "ワ", "worm", 400, 35, 32f, true, Element.Ice));
            Enemies.Add(Enemy("トロイ", "ト", "troy", 700, 60, 22f, false, Element.None));
        }

        static Unit Ally(string name, string sh, string art, Trait t, int cap) => new Unit
        {
            Name = name, Short = sh, Art = art, IsAlly = true, MaxHp = 300, Hp = 300, Trait = t, Capacity = cap, Speed = NoProgramSpeed,
        };

        static Unit Enemy(string name, string sh, string art, int hp, int atk, float speed, bool weak, Element w) => new Unit
        {
            Name = name, Short = sh, Art = art, IsAlly = false, MaxHp = hp, Hp = hp, Attack = atk, Speed = speed,
            HasWeakness = weak, Weakness = w,
        };

        public IEnumerable<Unit> All()
        {
            foreach (var u in Allies) yield return u;
            foreach (var u in Enemies) yield return u;
        }

        // チャージしていればメモリ容量が増える
        public int CapacityOf(Unit u) => (int)(u.Capacity * (u.Charged ? ChargeMemory : 1f));

        // リンクプログラムのメモリ容量（2人分）
        public int LinkCapacity(Unit main, Unit partner) => CapacityOf(main) + CapacityOf(partner);

        public void SetSpell(Unit ally, Spell spell)
        {
            ally.Spell = spell;
            if (spell != null) ally.Charging = false;
            UpdateSpeed(ally);
        }

        void UpdateSpeed(Unit ally)
        {
            var s = ally.Spell;
            if (s == null) { ally.Speed = NoProgramSpeed; return; }
            int cap = s.IsLink && ally.LinkPartner != null ? LinkCapacity(ally, ally.LinkPartner) : CapacityOf(ally);
            ally.Speed = Compiler.Speed(s.Memory, cap);
        }

        // チャージする／やめる。リンク中はできない
        public bool SetCharge(Unit ally, bool on)
        {
            if (ally.LinkPartner != null || ally.LinkedTo != null) return false;
            ally.Charging = on;
            if (on) ally.Spell = null;
            UpdateSpeed(ally);
            return true;
        }

        // リンクプログラムを送る。すでに同じ相手とリンクしているなら回数は減らない
        public bool Link(Unit main, Unit partner, Spell spell)
        {
            if (main.LinkPartner != partner)
            {
                if (LinksLeft <= 0) return false;
                if (main.LinkPartner != null) CancelLink(main);
                LinksLeft--;
            }
            main.LinkPartner = partner;
            partner.LinkedTo = main;
            partner.Spell = null;
            partner.Charging = false;
            main.Charging = false;
            spell.IsLink = true;
            spell.Partner = partner;
            main.Spell = spell;
            UpdateSpeed(main);
            UpdateSpeed(partner);
            return true;
        }

        // 送ったリンクをやめる（回数は戻る）
        public void CancelLink(Unit main)
        {
            var p = main.LinkPartner;
            if (p == null) return;
            p.LinkedTo = null;
            main.LinkPartner = null;
            if (main.Spell != null && main.Spell.IsLink) main.Spell = null;
            LinksLeft++;
            UpdateSpeed(main);
            UpdateSpeed(p);
        }

        public bool Won => !Enemies.Exists(e => e.Alive);
        public bool Lost => !Allies.Exists(a => a.Alive);

        // 生きている全員が 1 回以上行動したらターンが終わる（速いキャラは 2 回以上行動できる）
        public bool TurnOver
        {
            get
            {
                foreach (var u in All()) if (u.Alive && !u.ActedThisTurn) return false;
                return true;
            }
        }

        // 右端に着いていて、まだ行動していないキャラ（いちばん先に進んでいるもの）。いなければ null
        public Unit NextReady()
        {
            Unit best = null;
            foreach (var u in All())
                if (u.Alive && u.Gauge >= GaugeLength && (best == null || u.Gauge > best.Gauge)) best = u;
            return best;
        }

        public void Advance(float dt)
        {
            foreach (var u in All()) if (u.Alive) u.Gauge += u.Speed * dt;
        }

        // ターンの終わり。チャージは次のターンだけ効く。リンクはそのターンだけ。お知らせを返す
        public List<string> EndTurn()
        {
            var notes = new List<string>();
            Turn++;
            foreach (var u in All()) { u.Gauge = 0f; u.ActedThisTurn = false; }
            foreach (var a in Allies)
            {
                if (a.LinkPartner != null)
                {
                    a.LinkPartner.LinkedTo = null;
                    a.LinkPartner = null;
                    if (a.Spell != null && a.Spell.IsLink) { a.Spell = null; notes.Add(a.Name + "のリンクプログラムが終わった"); }
                }
            }
            foreach (var a in Allies)
            {
                a.Charged = a.Charging && a.Alive;
                a.Charging = false;
                if (a.Charged) notes.Add(a.Name + "：チャージ完了！ このターンはメモリ容量と攻撃力がアップ");
                if (a.Spell != null && a.Spell.Memory > CapacityOf(a))
                {
                    a.Spell = null;
                    notes.Add(a.Name + "：メモリ容量が戻ったので、プログラムを組み直してください");
                }
                UpdateSpeed(a);
            }
            return notes;
        }

        public Unit CurrentTarget()
        {
            if (TargetIndex >= 0 && TargetIndex < Enemies.Count && Enemies[TargetIndex].Alive) return Enemies[TargetIndex];
            foreach (var e in Enemies) if (e.Alive) return e;
            return null;
        }

        // ダメージの見込み（1発・1体あたり）。enemy が null なら弱点を考えない
        // ダメージ：リンクの相手の特性も効く。属性が 2 つなら、どちらかが得意・弱点なら効く
        public static int Damage(Unit ally, Spell s, Unit enemy)
        {
            float d = s.Power;
            bool fireUp = ally.Trait == Trait.FireUp || (s.Partner != null && s.Partner.Trait == Trait.FireUp);
            bool iceUp = ally.Trait == Trait.IceUp || (s.Partner != null && s.Partner.Trait == Trait.IceUp);
            if ((fireUp && s.Has(Element.Fire)) || (iceUp && s.Has(Element.Ice))) d *= 1.5f;   // 得意な属性（重ならない）
            if (enemy != null && enemy.HasWeakness && s.Has(enemy.Weakness)) d *= 1.5f;
            if (s.HasEffect && s.Effect == Effect.Blast) d *= 1.3f;
            if (ally.Charged) d *= ChargePower;
            d *= s.LinkBonus;
            return Math.Max(1, (int)Math.Round(d));
        }

        public ActionResult Act(Unit actor)
        {
            var r = new ActionResult { Actor = actor };
            actor.Gauge -= GaugeLength;
            actor.ActedThisTurn = true;
            if (actor.IsAlly) AllyAct(actor, r);
            else EnemyAct(actor, r);
            return r;
        }

        void AllyAct(Unit a, ActionResult r)
        {
            var s = a.Spell;
            if (a.LinkedTo != null) { r.Text = a.Name + "：" + a.LinkedTo.Name + "とリンク中（メモリを貸している）"; return; }
            if (a.Charging) { r.Text = a.Name + "：チャージ中… 次のターンにパワーアップ！"; return; }
            if (s == null) { r.Text = a.Name + "：プログラムが無いので何もしない"; return; }
            int total = 0;
            for (int shot = 0; shot < s.Shots; shot++)
            {
                var targets = new List<Unit>();
                if (s.Target == TargetType.All) { foreach (var e in Enemies) if (e.Alive) targets.Add(e); }
                else { var t = CurrentTarget(); if (t != null) targets.Add(t); }
                foreach (var e in targets)
                {
                    int d = Damage(a, s, e);
                    e.Hp = Math.Max(0, e.Hp - d);
                    total += d;
                    r.Hits.Add(new Hit { Target = e, Amount = d });
                    if (s.HasEffect && s.Effect == Effect.Slow) e.Gauge = Math.Max(0f, e.Gauge - 30f);
                }
            }
            r.Text = (s.IsLink && s.Partner != null ? a.Name + "＆" + s.Partner.Name + "：リンク" : a.Name + "：")
                   + "コンパイル完了！ 魔法（" + s.Describe() + "）発動！ 合計" + total + "ダメージ";
            if (s.HasEffect && s.Effect == Effect.Heal)
            {
                int h = Math.Min(a.MaxHp - a.Hp, s.Power / 2);
                a.Hp += h;
                r.Hits.Add(new Hit { Target = a, Amount = h, Heal = true });
                r.Text += "、HP を " + h + " 回復";
            }
        }

        void EnemyAct(Unit e, ActionResult r)
        {
            var alive = Allies.FindAll(x => x.Alive);
            if (alive.Count == 0) return;
            var t = alive[rng.Next(alive.Count)];
            int d = e.Attack + rng.Next(0, 11);
            t.Hp = Math.Max(0, t.Hp - d);
            r.Hits.Add(new Hit { Target = t, Amount = d });
            r.Text = e.Name + "の侵食！ " + t.Name + "に" + d + "ダメージ";
        }
    }
}
