#nullable enable

namespace Ezg.IconCsvGenerator.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    /// <summary>
    /// EditMode tests for <see cref="IconFilenameBuilder.BuildFromPattern"/>.
    ///
    /// These tests are pure-headless: they call only the static helper that resolves
    /// {token} patterns against a field dictionary — NO Unity AssetDatabase, NO editor,
    /// NO CSV file needed. Run in: Test Runner → EditMode.
    ///
    /// Covers:
    ///   - All 5 seed group filename patterns
    ///   - Whitespace-sanitization (Era = "Primal Wilds" → "PrimalWilds")
    ///   - Missing token → throws (no silent S_Unknown_ fallback)
    ///
    /// Gate: ALL tests must pass (green). Zero failures per development-principles.md.
    /// </summary>
    [TestFixture]
    internal sealed class IconFilenameBuilderTests
    {
        // ── Helper ────────────────────────────────────────────────────────────────

        private static Dictionary<string, string> Fields(params (string key, string value)[] pairs)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in pairs)
            {
                dict[key] = value;
            }
            return dict;
        }

        // ── Seed group: Currency ──────────────────────────────────────────────────

        [Test]
        public void Currency_Pattern_ResolvesSingleToken()
        {
            // Pattern: S_UI_Currency_{Id}
            // Expected: S_UI_Currency_Hammers.psd
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_UI_Currency_{Id}",
                Fields(("Id", "Hammers")));

            Assert.That(result, Is.EqualTo("S_UI_Currency_Hammers.psd"));
        }

        [Test]
        public void Currency_Pattern_AnotherRow()
        {
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_UI_Currency_{Id}",
                Fields(("Id", "Gold")));

            Assert.That(result, Is.EqualTo("S_UI_Currency_Gold.psd"));
        }

        // ── Seed group: Weapon ────────────────────────────────────────────────────

        [Test]
        public void Weapon_Pattern_ResolvesTwoTokens()
        {
            // Pattern: S_Icon_{ItemType}_{Id}_{Rarity}
            // Expected: S_Icon_Weapon_CrudeBoneClub_Common.psd
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Icon_{ItemType}_{Id}_{Rarity}",
                Fields(("ItemType", "Weapon"), ("Id", "CrudeBoneClub"), ("Rarity", "Common")));

            Assert.That(result, Is.EqualTo("S_Icon_Weapon_CrudeBoneClub_Common.psd"));
        }

        [Test]
        public void Weapon_Pattern_RareWeapon()
        {
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Icon_{ItemType}_{Id}_{Rarity}",
                Fields(("ItemType", "Weapon"), ("Id", "DragonBlade"), ("Rarity", "Legendary")));

            Assert.That(result, Is.EqualTo("S_Icon_Weapon_DragonBlade_Legendary.psd"));
        }

        // ── Seed group: EquipmentGear ─────────────────────────────────────────────

        [Test]
        public void EquipmentGear_Pattern_ResolvesHelmet()
        {
            // Pattern: S_Icon_{ItemType}_{Id}_{Rarity}
            // Same pattern as Weapon; ItemType differs (e.g. Helmet, Boots, etc.)
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Icon_{ItemType}_{Id}_{Rarity}",
                Fields(("ItemType", "Helmet"), ("Id", "ShadowHelm"), ("Rarity", "Rare")));

            Assert.That(result, Is.EqualTo("S_Icon_Helmet_ShadowHelm_Rare.psd"));
        }

        [Test]
        public void EquipmentGear_Pattern_ResolvesArmor()
        {
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Icon_{ItemType}_{Id}_{Rarity}",
                Fields(("ItemType", "Armor"), ("Id", "IronPlate"), ("Rarity", "Common")));

            Assert.That(result, Is.EqualTo("S_Icon_Armor_IronPlate_Common.psd"));
        }

        // ── Seed group: Skills ────────────────────────────────────────────────────

        [Test]
        public void Skills_Pattern_ResolvesSingleIdToken()
        {
            // Pattern: S_Skill_{Id}_Icon
            // Expected: S_Skill_PrimalShockwave_Icon.psd
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Skill_{Id}_Icon",
                Fields(("Id", "PrimalShockwave")));

            Assert.That(result, Is.EqualTo("S_Skill_PrimalShockwave_Icon.psd"));
        }

        [Test]
        public void Skills_Pattern_AnotherSkill()
        {
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Skill_{Id}_Icon",
                Fields(("Id", "FrostBolt")));

            Assert.That(result, Is.EqualTo("S_Skill_FrostBolt_Icon.psd"));
        }

        // ── Seed group: Realms (incl. whitespace-sanitization) ────────────────────

        [Test]
        public void Realms_Pattern_EraWithWhitespace_SanitizesToNoSpace()
        {
            // Pattern: S_Icon_Realm_{Era}
            // Era = "Primal Wilds" → spaces stripped → PrimalWilds
            // Expected: S_Icon_Realm_PrimalWilds.psd
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Icon_Realm_{Era}",
                Fields(("Era", "Primal Wilds")));

            Assert.That(result, Is.EqualTo("S_Icon_Realm_PrimalWilds.psd"),
                "Whitespace in Era token values must be stripped for a valid filename stem.");
        }

        [Test]
        public void Realms_Pattern_EraNoWhitespace_Unchanged()
        {
            // Era = "Volcanic" → no sanitization needed
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Icon_Realm_{Era}",
                Fields(("Era", "Volcanic")));

            Assert.That(result, Is.EqualTo("S_Icon_Realm_Volcanic.psd"));
        }

        [Test]
        public void Realms_Pattern_EraWithSpecialChars_Sanitized()
        {
            // Era = "Dark/Age?" → invalid filename chars stripped
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_Icon_Realm_{Era}",
                Fields(("Era", "Dark/Age?")));

            // '/' and '?' are invalid in filenames — must be stripped.
            var stem = result.Replace(".psd", string.Empty);
            Assert.That(stem, Does.Not.Contain("/"), "Forward-slash must be stripped from filename tokens.");
            Assert.That(stem, Does.Not.Contain("?"), "Question-mark must be stripped from filename tokens.");
        }

        // ── Missing token → throws (no S_Unknown_ fallback) ──────────────────────

        [Test]
        public void MissingToken_ThrowsInvalidOperationException()
        {
            // Pattern S_X_{Nope} with a field dict that has no "Nope" key.
            // Expectation: throws with an error naming the token ("Nope") and the row id.
            Assert.Throws<InvalidOperationException>(() =>
            {
                IconFilenameBuilder.BuildFromPattern(
                    "S_X_{Nope}",
                    Fields(("Id", "SomeRow"), ("SomethingElse", "value")),
                    rowId: "SomeRow");
            });
        }

        [Test]
        public void MissingToken_ErrorMessageContainsTokenName()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                IconFilenameBuilder.BuildFromPattern(
                    "S_{Missing}_{Id}",
                    Fields(("Id", "TestRow")),
                    rowId: "TestRow");
            });

            Assert.That(ex.Message, Does.Contain("Missing"),
                "Exception message must name the unknown token so the user knows which field is absent.");
        }

        // ── Psd extension auto-appended ───────────────────────────────────────────

        [Test]
        public void PsdExtension_AlwaysAppended_WhenPatternHasNoExtension()
        {
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_UI_Currency_{Id}",
                Fields(("Id", "Gold")));

            Assert.That(result, Does.EndWith(".psd"),
                "BuildFromPattern must always append .psd regardless of the pattern.");
        }

        [Test]
        public void PsdExtension_NotDoubled_WhenPatternAlreadyEndsPsd()
        {
            // If a pattern already has .psd explicitly, the builder must not double it.
            var result = IconFilenameBuilder.BuildFromPattern(
                "S_UI_Currency_{Id}.psd",
                Fields(("Id", "Gold")));

            var psdCount = System.Text.RegularExpressions.Regex.Matches(result, @"\.psd").Count;
            Assert.That(psdCount, Is.EqualTo(1),
                ".psd must appear exactly once in the output regardless of whether the pattern includes it.");
        }

        // ── Empty pattern ─────────────────────────────────────────────────────────

        [Test]
        public void EmptyPattern_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                IconFilenameBuilder.BuildFromPattern(
                    string.Empty,
                    Fields(("Id", "Gold")));
            });
        }
    }
}
