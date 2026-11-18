// 魔法プログラムバトルのルール（Unity に依存しない C#）
// ブロックの種類・メモリ・処理速度・ダメージの計算と、タイムラインで進む戦闘の進め方。
// 数値は仮。docs/決めたこと.md の「ゲームのルール」と合わせる

using System;
using System.Collections.Generic;

namespace MagicBattle
{
    // ブロックの種類。この順番で並べないと送信できない（追加効果は無くてもよい）
    public enum BlockKind { Element, Target, Power, Shots, Effect }

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

        public static readonly int[] PowerValues = { 10, 30, 50, 80, 120 };
        public static readonly int[] ShotValues = { 1, 2, 3 };

        public static int OptionCount(BlockKind k)
        {
            switch (k)
            {
                case BlockKind.Element: return 3;
                case BlockKind.Target: return 2;
                case BlockKind.Power: return PowerValues.Length;
                case BlockKind.Shots: return ShotValues.Length;
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
                default: return "追加効果";
            }
        }

        // ブロックが使うメモリ
        public static int Memory(BlockKind k, int v)
        {
            switch (k)
            {
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
        public TargetType Target;
        public int Power;
        public int Shots;
        public bool HasEffect;
        public Effect Effect;
        public int Memory;

        public string Describe()
        {
            string s = Blocks.Label(BlockKind.Element, (int)Element) + "・" + Blocks.Label(BlockKind.Target, (int)Target)
                     + "・威力" + Power + "×" + Shots + "回";
            if (HasEffect) s += "・" + Blocks.Label(BlockKind.Effect, (int)Effect);
            return s;
        }
    }

    public static class Compiler
    {
        public static int MemoryUsed(List<Block> code)
        {
            int m = 0;
            foreach (var b in code) m += Blocks.Memory(b.Kind, b.Value);
            return m;
        }

        // 送信できるか確かめる。できないときは理由を message に入れて false
        public static bool Check(List<Block> code, int capacity, out string message)
        {
            if (code.Count == 0) { message = "ブロックが1つもありません"; return false; }
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].Kind == code[i - 1].Kind)
                {
                    message = Blocks.KindName(code[i].Kind) + "のブロックが2つあります";
                    return false;
                }
                if (code[i].Kind < code[i - 1].Kind)
                {
                    message = "順番が違います（" + Blocks.KindName(code[i].Kind) + "は"
                            + Blocks.KindName(code[i - 1].Kind) + "より前）";
                    return false;
                }
            }
            for (var k = BlockKind.Element; k <= BlockKind.Shots; k++)
            {
                if (!code.Exists(b => b.Kind == k)) { message = Blocks.KindName(k) + "のブロックがありません"; return false; }
            }
            int used = MemoryUsed(code);
            if (used > capacity) { message = "メモリオーバー（" + used + " / " + capacity + "）"; return false; }
            message = "送信できます";
            return true;
        }

        // Check が通った code から魔法を作る
        public static Spell Build(List<Block> code)
        {
            var s = new Spell { Memory = MemoryUsed(code) };
            foreach (var b in code)
            {
                switch (b.Kind)
                {
                    case BlockKind.Element: s.Element = (Element)b.Value; break;
                    case BlockKind.Target: s.Target = (TargetType)b.Value; break;
                    case BlockKind.Power: s.Power = Blocks.PowerValues[b.Value]; break;
                    case BlockKind.Shots: s.Shots = Blocks.ShotValues[b.Value]; break;
                    case BlockKind.Effect: s.HasEffect = true; s.Effect = (Effect)b.Value; break;
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
        public bool IsAlly;
        public int MaxHp, Hp;
        public float Gauge;       // タイムライン上の位置。Battle.GaugeLength で行動する
        public float Speed;
        public bool ActedThisTurn;

        // 味方だけ
        public Trait Trait;
        public int Capacity;      // メモリ容量
        public Spell Spell;       // 送信した魔法。null なら何もしない

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
        readonly Random rng;

        public Battle(int seed)
        {
            rng = new Random(seed);
            Allies.Add(Ally("レッド", "レ", Trait.FireUp, 1000));
            Allies.Add(Ally("ブルー", "ブ", Trait.IceUp, 1000));
            Allies.Add(Ally("イエロー", "イ", Trait.MemoryUp, 1500));
            Enemies.Add(Enemy("ゴブリンA", "A", 400, 35, 30f, true, Element.Fire));
            Enemies.Add(Enemy("ゴブリンB", "B", 400, 35, 32f, true, Element.Ice));
            Enemies.Add(Enemy("オーガ", "オ", 700, 60, 22f, false, Element.None));
        }

        static Unit Ally(string name, string sh, Trait t, int cap) => new Unit
        {
            Name = name, Short = sh, IsAlly = true, MaxHp = 300, Hp = 300, Trait = t, Capacity = cap, Speed = NoProgramSpeed,
        };

        static Unit Enemy(string name, string sh, int hp, int atk, float speed, bool weak, Element w) => new Unit
        {
            Name = name, Short = sh, IsAlly = false, MaxHp = hp, Hp = hp, Attack = atk, Speed = speed,
            HasWeakness = weak, Weakness = w,
        };

        public IEnumerable<Unit> All()
        {
            foreach (var u in Allies) yield return u;
            foreach (var u in Enemies) yield return u;
        }

        public void SetSpell(Unit ally, Spell spell)
        {
            ally.Spell = spell;
            ally.Speed = spell == null ? NoProgramSpeed : Compiler.Speed(spell.Memory, ally.Capacity);
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

        public void EndTurn()
        {
            Turn++;
            foreach (var u in All()) { u.Gauge = 0f; u.ActedThisTurn = false; }
        }

        public Unit CurrentTarget()
        {
            if (TargetIndex >= 0 && TargetIndex < Enemies.Count && Enemies[TargetIndex].Alive) return Enemies[TargetIndex];
            foreach (var e in Enemies) if (e.Alive) return e;
            return null;
        }

        // ダメージの見込み（1発・1体あたり）。enemy が null なら弱点を考えない
        public static int Damage(Unit ally, Spell s, Unit enemy)
        {
            float d = s.Power;
            if (ally.Trait == Trait.FireUp && s.Element == Element.Fire) d *= 1.5f;
            if (ally.Trait == Trait.IceUp && s.Element == Element.Ice) d *= 1.5f;
            if (enemy != null && enemy.HasWeakness && s.Element == enemy.Weakness) d *= 1.5f;
            if (s.HasEffect && s.Effect == Effect.Blast) d *= 1.3f;
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
            r.Text = a.Name + "の魔法（" + s.Describe() + "）！ 合計" + total + "ダメージ";
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
            r.Text = e.Name + "の攻撃！ " + t.Name + "に" + d + "ダメージ";
        }
    }
}
