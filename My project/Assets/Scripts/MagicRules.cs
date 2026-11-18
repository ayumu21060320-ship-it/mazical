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

    // ウイルスの特性
    //   Regen:      増殖。ターンの終わりに HP が 15% 回復する
    //   AllAttack:  一斉送信。味方全員を攻撃する（1人あたりは弱め）
    //   Lock:       ロック。攻撃した味方の次のターンのメモリ容量を 30% 減らす
    //   Scatter:    分散。「1体」の魔法は半分しか効かないが、「全体」の魔法は 1.5 倍効く
    //   Firewall:   ファイアウォール。属性の無い魔法（無）以外は効きにくい（炎・氷に耐性）
    public enum VirusAbility { None, Regen, AllAttack, Lock, Scatter, Firewall }

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

        public bool LockPending;  // ロックされた（次のターンにメモリ容量が減る）
        public bool Locked;       // このターンはメモリ容量が減っている

        // 敵だけ
        public Element[] Weak = new Element[0];     // 弱点（1.5 倍）
        public Element[] Resist = new Element[0];   // 耐性（0.5 倍）
        public VirusAbility Ability;
        public int Attack;

        public static string AbilityName(VirusAbility a)
        {
            switch (a)
            {
                case VirusAbility.Regen: return "増殖";
                case VirusAbility.AllAttack: return "一斉送信";
                case VirusAbility.Lock: return "ロック";
                case VirusAbility.Scatter: return "分散";
                case VirusAbility.Firewall: return "ファイアウォール";
                default: return "なし";
            }
        }

        public static string AbilityText(VirusAbility a)
        {
            switch (a)
            {
                case VirusAbility.Regen: return "ターンの終わりに HP が 15% 回復する";
                case VirusAbility.AllAttack: return "味方全員を攻撃する（1人あたりは弱め）";
                case VirusAbility.Lock: return "攻撃した味方の、次のターンのメモリ容量を 30% 減らす";
                case VirusAbility.Scatter: return "「1体」の魔法は半分しか効かない。「全体」の魔法は 1.5 倍効く";
                case VirusAbility.Firewall: return "炎も氷も効きにくい。属性「無」の魔法でこわそう";
                default: return "特別な力は無い";
            }
        }

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
        public bool Weak, Resist;   // 弱点をついた／効きにくかった
    }

    public class ActionResult
    {
        public Unit Actor;
        public readonly List<Hit> Hits = new List<Hit>();
        public string Text;
    }

    // AI ロボット（味方）の情報。キャラクター画面と戦闘で使う
    public class RobotInfo
    {
        public string Name, Short, Art, Description;
        public Trait Trait;
        public int Capacity, Hp;
    }

    public static class Characters
    {
        public static readonly RobotInfo[] All =
        {
            new RobotInfo { Name = "レッド", Short = "レ", Art = "red", Trait = Trait.FireUp, Capacity = 1000, Hp = 300,
                Description = "元気いっぱいの炎の魔法使い。炎属性の魔法の威力が 1.5 倍" },
            new RobotInfo { Name = "ブルー", Short = "ブ", Art = "blue", Trait = Trait.IceUp, Capacity = 1000, Hp = 300,
                Description = "冷静な氷の魔法使い。氷属性の魔法の威力が 1.5 倍" },
            new RobotInfo { Name = "イエロー", Short = "イ", Art = "yellow", Trait = Trait.MemoryUp, Capacity = 1500, Hp = 300,
                Description = "強気な天才。メモリ容量が大きく、長いプログラムを組める" },
        };
    }

    // 依頼（ステージ）。だれのパソコンに、どんなウイルスがいるか
    public class EnemySpec
    {
        public string Name, Short, Art;
        public int Hp, Attack;
        public float Speed;
        public Element[] Weak, Resist;
        public VirusAbility Ability;
        public string Description;
        public EnemySpec(string name, string sh, string art, int hp, int atk, float speed, Element[] weak, Element[] resist, VirusAbility ability, string desc)
        {
            Name = name; Short = sh; Art = art; Hp = hp; Attack = atk; Speed = speed;
            Weak = weak; Resist = resist; Ability = ability; Description = desc;
        }
        public EnemySpec WithHp(int hp) => new EnemySpec(Name, Short, Art, hp, Attack, Speed, Weak, Resist, Ability, Description);
    }

    // ウイルスの種類（ウイルス図鑑と依頼で使う）
    public static class Viruses
    {
        static Element[] E(params Element[] e) => e;
        public static readonly EnemySpec Bug = new EnemySpec("バグ", "バ", "bug", 400, 35, 30f, E(Element.Fire), E(Element.Ice), VirusAbility.None,
            "いちばんよく見るウイルス。プログラムをかじって、文字をめちゃくちゃにする");
        public static readonly EnemySpec Worm = new EnemySpec("ワーム", "ワ", "worm", 400, 35, 32f, E(Element.Ice), E(Element.Fire), VirusAbility.Regen,
            "ファイルを勝手にコピーして増えていく。早めに倒さないと回復してしまう");
        public static readonly EnemySpec Spam = new EnemySpec("スパム", "ス", "spam", 220, 25, 44f, E(Element.Fire), E(), VirusAbility.Scatter,
            "迷惑メールのウイルス。すばしっこくて、まとめて攻撃しないと効きにくい");
        public static readonly EnemySpec Ransom = new EnemySpec("ランサム", "ラ", "ransom", 500, 40, 26f, E(Element.Ice), E(Element.Fire), VirusAbility.Lock,
            "データにカギをかけて人質にする。攻撃された味方はメモリが減ってしまう");
        public static readonly EnemySpec Troy = new EnemySpec("トロイ", "ト", "troy", 700, 45, 22f, E(Element.None), E(Element.Fire, Element.Ice), VirusAbility.AllAttack,
            "安全なプログラムのふりをして入りこむ大物。炎も氷も効きにくく、全員を攻撃してくる");
        public static readonly EnemySpec[] All = { Bug, Worm, Spam, Ransom, Troy };
    }

    public class Request
    {
        public string Id, Client, Title, Message;
        public int Stars;            // むずかしさ（★の数）
        public EnemySpec[] Enemies;
    }

    public static class Requests
    {
        static EnemySpec Bug(int hp = 400) => Viruses.Bug.WithHp(hp);
        static EnemySpec Worm(int hp = 400) => Viruses.Worm.WithHp(hp);
        static EnemySpec Spam(int hp = 220) => Viruses.Spam.WithHp(hp);
        static EnemySpec Ransom(int hp = 500) => Viruses.Ransom.WithHp(hp);
        static EnemySpec Troy(int hp = 700) => Viruses.Troy.WithHp(hp);

        public static readonly Request[] All =
        {
            new Request
            {
                Id = "r1", Client = "ゆうき", Title = "宿題のデータがバグだらけ！", Stars = 1,
                Message = "明日出す宿題のファイルを開いたら、文字がめちゃくちゃに…。虫みたいなのが画面をはってる！",
                Enemies = new[] { Bug(250), Bug(250) },
            },
            new Request
            {
                Id = "r2", Client = "さくら", Title = "写真フォルダにワームが…", Stars = 2,
                Message = "旅行の写真がどんどんコピーされて、パソコンがパンパン！ 紫のにょろにょろが増えてるの",
                Enemies = new[] { Worm(350), Spam(), Worm(350) },
            },
            new Request
            {
                Id = "r4", Client = "こうた", Title = "ゲームのセーブデータにカギが！", Stars = 2,
                Message = "セーブデータを開こうとしたら「返してほしければ…」って出てきた！ 金色のカギのやつが2体いる",
                Enemies = new[] { Ransom(450), Spam(200), Ransom(450) },
            },
            new Request
            {
                Id = "r3", Client = "たなか先生", Title = "成績データが乗っ取られた！", Stars = 3,
                Message = "職員室のパソコンに大きな一つ目のウイルスが…。成績のデータを人質にとられています。至急たのみます",
                Enemies = new[] { Bug(), Worm(), Troy() },
            },
        };
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

        public Battle(int seed) : this(seed, Requests.All[Requests.All.Length - 1]) { }

        public Battle(int seed, Request request)
        {
            rng = new Random(seed);
            foreach (var c in Characters.All)
            {
                var a = Ally(c.Name, c.Short, c.Art, c.Trait, c.Capacity);
                a.MaxHp = a.Hp = c.Hp;
                Allies.Add(a);
            }
            // 敵は依頼主のパソコンに入りこんだウイルス
            foreach (var e in request.Enemies)
                Enemies.Add(new Unit
                {
                    Name = e.Name, Short = e.Short, Art = e.Art, IsAlly = false, MaxHp = e.Hp, Hp = e.Hp, Attack = e.Attack, Speed = e.Speed,
                    Weak = e.Weak, Resist = e.Resist, Ability = e.Ability,
                });
        }

        static Unit Ally(string name, string sh, string art, Trait t, int cap) => new Unit
        {
            Name = name, Short = sh, Art = art, IsAlly = true, MaxHp = 300, Hp = 300, Trait = t, Capacity = cap, Speed = NoProgramSpeed,
        };


        public IEnumerable<Unit> All()
        {
            foreach (var u in Allies) yield return u;
            foreach (var u in Enemies) yield return u;
        }

        // チャージしていればメモリ容量が増える
        public int CapacityOf(Unit u) => (int)(u.Capacity * (u.Charged ? ChargeMemory : 1f) * (u.Locked ? 0.7f : 1f));

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
            foreach (var e in Enemies)
            {
                if (e.Alive && e.Ability == VirusAbility.Regen && e.Hp < e.MaxHp)
                {
                    int h = Math.Min(e.MaxHp - e.Hp, (int)(e.MaxHp * 0.15f));
                    e.Hp += h;
                    notes.Add(e.Name + "が増殖して HP が " + h + " 回復した");
                }
            }
            foreach (var a in Allies)
            {
                a.Locked = a.LockPending && a.Alive;
                a.LockPending = false;
                if (a.Locked) notes.Add(a.Name + "はロックされていて、このターンはメモリ容量が 30% 少ない");
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
            d *= Effectiveness(s, enemy);
            if (s.HasEffect && s.Effect == Effect.Blast) d *= 1.3f;
            if (ally.Charged) d *= ChargePower;
            d *= s.LinkBonus;
            return Math.Max(1, (int)Math.Round(d));
        }

        // 相性：弱点 1.5 倍、耐性 0.5 倍（両方なら打ち消し）。分散の特性は「1体」0.5 倍・「全体」1.5 倍
        public static float Effectiveness(Spell s, Unit enemy)
        {
            if (enemy == null) return 1f;
            float k = 1f;
            bool weak = false, resist = false;
            foreach (var w in enemy.Weak) if (s.Has(w)) weak = true;
            foreach (var r in enemy.Resist) if (s.Has(r)) resist = true;
            if (weak && !resist) k *= 1.5f;
            if (resist && !weak) k *= 0.5f;
            if (enemy.Ability == VirusAbility.Scatter) k *= s.Target == TargetType.One ? 0.5f : 1.5f;
            return k;
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
                    float eff = Effectiveness(s, e);
                    r.Hits.Add(new Hit { Target = e, Amount = d, Weak = eff > 1.01f, Resist = eff < 0.99f });
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
            if (e.Ability == VirusAbility.AllAttack)
            {
                int total = 0;
                foreach (var a in alive)
                {
                    int dd = (int)(e.Attack * 0.6f) + rng.Next(0, 8);
                    a.Hp = Math.Max(0, a.Hp - dd);
                    total += dd;
                    r.Hits.Add(new Hit { Target = a, Amount = dd });
                }
                r.Text = e.Name + "の一斉送信！ 味方全員に合計" + total + "ダメージ";
                return;
            }
            var t = alive[rng.Next(alive.Count)];
            int d = e.Attack + rng.Next(0, 11);
            t.Hp = Math.Max(0, t.Hp - d);
            r.Hits.Add(new Hit { Target = t, Amount = d });
            r.Text = e.Name + "の侵食！ " + t.Name + "に" + d + "ダメージ";
            if (e.Ability == VirusAbility.Lock && t.Alive)
            {
                t.LockPending = true;
                r.Text += "。" + t.Name + "のメモリにカギがかかった（次のターン 30% 減）";
            }
        }
    }
}
