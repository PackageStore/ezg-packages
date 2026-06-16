namespace Ezg.Core.Utils
{
public class EnumBase
{
    /// <summary>
    ///     Kiểu stat modifiable
    /// </summary>
    public enum StatModTypes
    {
        None = 0,
        Number = 1,
        Percent = 2,
        NumberTotal = 3,
        PercentTotal = 4
    }

    public enum AnimTypes
    {
        Animation,
        Spine
    }

    public enum AttackTypes
    {
        Physic = 0,
        Magic = 1
    }

    public enum BattleControlTypes
    {
        LockPosition,
        DragPosition
    }

    /// <summary>
    ///     Bộ đếm thời gian xuôi ngược trong trò chơi
    /// </summary>
    public enum BattleCounterTypes
    {
        TimeIncrement, //Thời gian tăng dần
        TimeDecrement, //Giảm dần
        EnemyIncrement, //Đếm quái tăng dần
        EnemyDecrement //Giảm dần
    }

    public enum BattleLevelDifficult
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Crazy = 3,
        Insane = 4
    }

    public enum BattleModes
    {
        Campaign,
        RaidBoss,
        Adventure,
        Trial,
        SwordSpin,
        Dungeon,
        Arena
    }

    public enum BattleWarningTypes
    {
        MiniBoss,
        Boss,
        CoinRush,
        BigWave,
        Wave,
        EndSpawn,
        Win,
        Lose
    }

    public enum BonusMechanicTypes
    {
        None,
        Stat,
        StatAll,
        Skill,
        Passive
    }

    public enum CampaignModes
    {
        Normal,

        Hard
        //Nightmare,
    }

    public enum DamageTypes
    {
        None,
        Normal,
        DOT
    }

    public enum Directions
    {
        None = 0,
        Auto = 1,
        Custom = 2,
        Random = 3,
        RandomAngleInDirection = 4
    }

    public enum DurationTypes
    {
        time = 0,
        numberAttack = 1
    }

    public enum EnemyAppearTypes
    {
        Normal = 0, //Random ngoài camera
        TopLeft = 1,
        TopMid = 2,
        TopRight = 3,
        Right = 4,
        BotRight = 5,
        BotMid = 6,
        BotLeft = 7,
        Left = 8,
        Circle = 9, //Xuất hiện bao vây xung quanh nhân vật
        TopRandom = 10,
        LeftRandom = 11,
        RightRandom = 12,
        BotRandom = 13
    }

    public enum EnemyMoveTypes
    {
        None = 0,
        Normal = 1, //Tar get player
        Fly = 1, //Đi thẳng xuyên qua player khi xuất hiện
        Charge = 3 //Đi thẳng xuyên qua player khi xuất hiện
    }

    public enum EquipmentTypes
    {
        None,
        Helmet,
        Armor,
        Boots,
        Gloves,
        Amulet,
        Ring
    }

    public enum GameLayers
    {
        Map = 6,
        MapObject = 7,
        Attack = 8,
        Magnet = 9,
        Character = 10,
        OutScreen = 11
    }

    public enum GameTags
    {
        MainHero,
        Enemy,
        AttackHero,
        AttackEnemy,
        ItemDropExp,
        ScreenLine,
        MapRange, //Vùng giới hạn của map
        NotTarget
    }

    public enum HealChangeTypes
    {
        Normal,
        CritDamage,
        Regend,
        InstantKill
    }

    public enum HeroClass
    {
        None,
        One,
        Two,
        Three,
        Four,
        Five
    }

    public enum ItemDropTypes
    {
        None,
        Exp1,
        Exp2,
        Exp3,
        Exp4,
        Exp5,
        Exp6,
        Exp7,
        Exp8,
        Gold1,
        Gold2,
        Gold3,
        Gold4,
        Gold5,
        Gold6,
        Health,
        Explode,
        Magnet,
        Freeze,
        BossChest
    }

    public enum ItemRarities
    {
        None = 0,
        Common = 1,
        Uncommon = 2,
        Rare = 3,
        Epic = 4,
        Legend = 5,
        Divine = 6
    }

    public enum ItemTypes
    {
        None = 0,
        Equipment = 1,
        Booster = 10,
        PowerUp = 11,
        Gacha = 12,
        Pack = 13,
        Relic = 14,
        Skill = 15,
        Jewel = 16,
        Misc = 20
    }

    public enum LayerSorting
    {
        Main,
        Modal,
        UI,
        CurrencyBar,
        Overlays,
        Tutorial,
        Toast
    }

    public enum MapWaveTypes
    {
        None,
        Normal,
        Boss,
        CoinBonus
    }

    public enum MechanicTypes
    {
        none,

        direction,
        duration,
        target_from,
        target_to,
        range,
        detect_range,
        damage,
        fire_rate,
        rotate_speed,
        cooldown,
        cooldown_after_projectile,
        cooldown_after_duration,
        projectile_number,
        projectile_speed,
        projectile_size,
        target_radius,

        effect_1,
        effect_2,
        effect_3,
        effect_4,
        effect_5,
        effect_6,
        effect_7,
        effect_8,
        effect_9,
        effect_10,

        stats_1,
        stats_2,
        stats_3,
        stats_4,
        stats_5,
        stats_6,
        stats_7,
        stats_8,
        stats_9,
        stats_10,

        skill_stats_1,
        skill_stats_2,
        skill_stats_3,
        skill_stats_4,
        skill_stats_5,
        skill_stats_6,
        skill_stats_7,
        skill_stats_8,
        skill_stats_9,
        skill_stats_10,

        custom_values_1,
        custom_values_2,
        custom_values_3,
        custom_values_4,
        custom_values_5,

        stat_limit_1,
        stat_limit_2,
        stat_limit_3,
        stat_limit_4,
        stat_limit_5,
        stat_limit_6,
        stat_limit_7,
        stat_limit_8,
        stat_limit_9,
        stat_limit_10,

        summon_1,
        summon_2,
        summon_3,
        summon_4,
        summon_5,

        custom_values_6,
        custom_values_7,
        custom_values_8,
        custom_values_9,
        custom_values_10
    }

    public enum MoneyTypes
    {
        None = 0,
        Gold = 1,
        Diamonds = 2,
        Energy = 3,
        InfinityEnergy = 4,
        Exp = 5,
        SkipTime = 6,
        Star = 7,
        Exchange = 8,
        AddSlotInventory = 9,
        CurrencyInfinityPack = 10,


        Ads = 1000,
        Cash = 1001,

        TokenSpeedFeastRace = 2000,
        TokenPizzaTowerRace = 2001,
        HammerPetalPlateParty = 2002,
        HatFortuneMeetsCookie = 2003,
        TextFortuneMeetsCookie = 2004,
        ChopsTicksFortuneMeetsCookie = 2005,

        Level = 55555
    }

    public enum OpenType
    {
        X1,
        X10
    }

    public enum PlayerDataTypes
    {
        None,
        Settings,
        BattleData,
        AnalyzeData
    }

    public enum PowerUpTypes
    {
        None,
        Shockwave,
        Magnet,
        Shield,
        IcePotion,
        GoldRush,
        DragonPet,
        Rage,
        InstantUltimate
    }

    public enum QuestBonusTypes
    {
        Bonus,
        Set
    }

    public enum QuestContidions
    {
        Milestone,
        SkillId,
        HeroId
    }

    public enum QuestStyles
    {
        Daily,
        Weekly
    }

    public enum QuestTypes
    {
        None = 0,
        UpgradeTalent = 1,
        CompleteRun = 2,
        HealWithAds = 3,
        OpenPremium = 4,
        Revive = 5,
        DefeatEnemies = 6,
        DefeatEnemiesBySkill = 7,
        DefeatEnemiesByHero = 8,
        CompleteRunByHero = 9,
        WatchAds = 10,
        PurchaseInShop = 11,
        FinishDailyQuest = 12,
        Login = 13,
        SpendGem = 14,
        CraftJewel = 15,
        MergeEquipment = 16,
        UpgradeHero = 17,
        OpenNormal = 18,
        ClaimLoginStreak = 19,
        WatchAdsDaily = 20,
        WatchAdsWeekly = 21,
        HavePet = 22,
        Travel = 23,
        CompleteRunHavePet = 24,
        EventWatchAds = 25
    }

    /// <summary>
    ///     Loại tài nguyên. Trước đây là enum, nay chuyển sang static class chứa const int
    ///     để các trường resType/costType/... lưu dưới dạng int (giá trị giữ nguyên).
    /// </summary>
    public static class ResourceTypes
    {
        public const int None = 0;
        public const int Money = 1;
        public const int Item = 2;

        //Hero = 3,
        //Skill = 4,
        //Book = 5,
        public const int Feature = 6;
        public const int Package = 7;

        //Pet = 8,
        //Exp = 9,
        public const int Level = 10;

        /// <summary>Danh sách giá trị hợp lệ (thay cho Enum.GetValues).</summary>
        public static readonly int[] All = { None, Money, Item, Feature, Package, Level };

        /// <summary>Tên hiển thị tương ứng với <see cref="All"/> (thay cho Enum.GetNames).</summary>
        public static readonly string[] Names = { "none", "money", "item", "feature", "package", "level" };

        /// <summary>int → tên viết thường (giữ nguyên key localization/analytics cũ).</summary>
        public static string GetName(int value)
        {
            switch (value)
            {
                case Money: return "money";
                case Item: return "item";
                case Feature: return "feature";
                case Package: return "package";
                case Level: return "level";
                default: return "none";
            }
        }
    }

    public enum SettingTypes
    {
        None,
        Notifications,
        Vibrate,
        Music,
        Sound,
        Language,
        Screen,
        Control,
        SyncData
    }

    /// <summary>
    ///     Tác dụng của stat cho các loại skill
    /// </summary>
    public enum SkillForgeTypes
    {
        Base,
        Evo,
        BaseEvo,
        All
    }

    /// <summary>
    ///     Loại skill
    /// </summary>
    public enum SkillTypes
    {
        Magic, //Magic
        Physic, //Physics
        Passive
    }

    /// <summary>
    ///     Kiểu show skill định hướng
    /// </summary>
    public enum SkillViewTypes
    {
        None = 0,
        Circle = 1,
        Arrow = 2
    }

    public enum TargetFrom
    {
        Self = 0,
        RandomEnemy = 1,
        NearestEnemy = 2,
        RandomLocation = 3,
        MainTower = 4,
        RandomOutCam = 5,
        RandomOutCamTop = 6,
        Custom = 7,
        Pet
    }

    public enum TargetTo
    {
        Self = 0,
        RandomEnemy = 1,
        NearestEnemy = 2,
        RandomLocation = 3,
        TargetFrom = 4,
        Custom = 5,
        FollowJoystick = 6
    }
}
}
