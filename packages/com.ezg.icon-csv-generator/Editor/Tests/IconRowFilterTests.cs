#nullable enable

namespace Ezg.IconCsvGenerator.Editor.Tests
{
    using System.Collections.Generic;
    using NUnit.Framework;

    /// <summary>
    /// EditMode tests for <see cref="IconCsvLoader.PassesFilter"/>.
    ///
    /// Pure-headless: calls only the internal static helper directly — no CSV file,
    /// no AssetDatabase, no Unity editor needed. Run in: Test Runner → EditMode.
    ///
    /// Contract tested:
    ///   - None  → every row passes unconditionally
    ///   - Equals(column, value) → only rows whose field matches pass; case-sensitive per
    ///     the standard CSV data (ItemType = "Weapon" != "weapon")
    ///   - NotEquals(column, value) → inverse of Equals; weapon rows excluded, gear rows pass
    ///   - Missing filterColumn with Equals/NotEquals → contract: row FAILS (empty string
    ///     never equals a non-empty filterValue)
    ///   - Missing filterColumn with None → row passes (filter column is irrelevant for None)
    ///
    /// Gate: ALL tests must pass (green). Zero failures per development-principles.md.
    /// </summary>
    [TestFixture]
    internal sealed class IconRowFilterTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        private static IReadOnlyDictionary<string, string> EquipmentFields(string itemType, string id = "TestId")
        {
            return new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Id",       id       },
                { "ItemType", itemType },
                { "Rarity",   "Common" },
            };
        }

        private static IReadOnlyDictionary<string, string> CurrencyFields(string id = "Gold")
        {
            return new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Id",           id      },
                { "CurrencyType", "Soft"  },
            };
        }

        private static IconCsvGroup MakeGroup(
            IconRowFilterMode mode,
            string filterColumn = "ItemType",
            string filterValue  = "Weapon")
        {
            return new IconCsvGroup
            {
                groupName    = "Test",
                filterColumn = filterColumn,
                filterMode   = mode,
                filterValue  = filterValue,
            };
        }

        // ── FilterMode.None ───────────────────────────────────────────────────────

        [Test]
        public void None_WeaponRow_Passes()
        {
            var group = MakeGroup(IconRowFilterMode.None);
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("Weapon"), group), Is.True,
                "None mode must pass all rows unconditionally.");
        }

        [Test]
        public void None_GearRow_Passes()
        {
            var group = MakeGroup(IconRowFilterMode.None);
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("Helmet"), group), Is.True,
                "None mode must pass all rows unconditionally.");
        }

        [Test]
        public void None_CurrencyRow_Passes()
        {
            var group = MakeGroup(IconRowFilterMode.None, filterColumn: "NonExistent", filterValue: "");
            Assert.That(IconCsvLoader.PassesFilter(CurrencyFields(), group), Is.True,
                "None mode must pass all rows even when filterColumn doesn't exist in the row.");
        }

        // ── FilterMode.Equals ─────────────────────────────────────────────────────

        [Test]
        public void Equals_WeaponRow_Passes()
        {
            var group = MakeGroup(IconRowFilterMode.Equals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("Weapon"), group), Is.True,
                "Equals(ItemType, Weapon) must pass a weapon row.");
        }

        [Test]
        public void Equals_GearRow_DoesNotPass()
        {
            var group = MakeGroup(IconRowFilterMode.Equals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("Helmet"), group), Is.False,
                "Equals(ItemType, Weapon) must reject a Helmet row.");
        }

        [Test]
        public void Equals_GearRow_BootsDoesNotPass()
        {
            var group = MakeGroup(IconRowFilterMode.Equals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("Boots"), group), Is.False,
                "Equals(ItemType, Weapon) must reject a Boots row.");
        }

        [Test]
        public void Equals_CaseSensitive_LowercaseWeaponDoesNotPass()
        {
            // CSV data uses "Weapon" not "weapon"; case-sensitive match.
            var group = MakeGroup(IconRowFilterMode.Equals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("weapon"), group), Is.False,
                "Equals filter is case-sensitive (per CSV data convention).");
        }

        [Test]
        public void Equals_MissingFilterColumn_DoesNotPass()
        {
            // Row has no "ItemType" key; empty string != "Weapon" → fails.
            var fields = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Id",           "NoCategoryRow" },
                { "CurrencyType", "Soft"          },
            };
            var group = MakeGroup(IconRowFilterMode.Equals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(fields, group), Is.False,
                "Equals filter with a missing filterColumn key must not pass (empty string != non-empty filterValue).");
        }

        // ── FilterMode.NotEquals ──────────────────────────────────────────────────

        [Test]
        public void NotEquals_WeaponRow_DoesNotPass()
        {
            var group = MakeGroup(IconRowFilterMode.NotEquals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("Weapon"), group), Is.False,
                "NotEquals(ItemType, Weapon) must reject weapon rows (the Weapon group picks those up).");
        }

        [Test]
        public void NotEquals_GearRow_Passes()
        {
            var group = MakeGroup(IconRowFilterMode.NotEquals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("Helmet"), group), Is.True,
                "NotEquals(ItemType, Weapon) must pass helmet rows.");
        }

        [Test]
        public void NotEquals_BootsRow_Passes()
        {
            var group = MakeGroup(IconRowFilterMode.NotEquals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(EquipmentFields("Boots"), group), Is.True,
                "NotEquals(ItemType, Weapon) must pass boots rows.");
        }

        [Test]
        public void NotEquals_MissingFilterColumn_Passes()
        {
            // Row has no "ItemType" → empty string != "Weapon" → passes the NotEquals filter.
            // This is intentional: a row without the filter column is NOT equal to the
            // filter value, so it passes a NotEquals predicate.
            var fields = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Id",           "NoCategoryRow" },
                { "CurrencyType", "Soft"          },
            };
            var group = MakeGroup(IconRowFilterMode.NotEquals, filterColumn: "ItemType", filterValue: "Weapon");
            Assert.That(IconCsvLoader.PassesFilter(fields, group), Is.True,
                "NotEquals with a missing filterColumn: empty-string != filterValue → row passes.");
        }

        // ── Equipments split correctness ──────────────────────────────────────────
        // The Weapon + EquipmentGear groups together must cover ALL equipment rows
        // with no overlap and no gaps (bijective partition by ItemType == "Weapon").

        [Test]
        public void WeaponAndGear_AreComplementaryPartitions_NoGap()
        {
            var weaponGroup = MakeGroup(IconRowFilterMode.Equals,    filterColumn: "ItemType", filterValue: "Weapon");
            var gearGroup   = MakeGroup(IconRowFilterMode.NotEquals, filterColumn: "ItemType", filterValue: "Weapon");

            // Every row that fails Weapon passes Gear, and vice versa.
            var itemTypes = new[] { "Weapon", "Helmet", "Boots", "Armor", "Gloves", "Accessory" };
            foreach (var itemType in itemTypes)
            {
                var fields = EquipmentFields(itemType);
                var passesWeapon = IconCsvLoader.PassesFilter(fields, weaponGroup);
                var passesGear   = IconCsvLoader.PassesFilter(fields, gearGroup);

                Assert.That(passesWeapon ^ passesGear, Is.True,
                    $"ItemType='{itemType}' must pass exactly ONE of (Weapon group, Gear group). " +
                    $"passesWeapon={passesWeapon}, passesGear={passesGear}.");
            }
        }
    }
}
