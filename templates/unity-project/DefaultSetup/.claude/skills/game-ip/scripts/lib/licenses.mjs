// Nhận diện license từ nội dung file license / header source / mô tả license trong font, và tra bảng data/licenses.json.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const DATA = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "data", "licenses.json");
let table = null;

/** Bảng license (id → thông tin). */
export function licenseTable() {
  if (!table) table = JSON.parse(fs.readFileSync(DATA, "utf8")).licenses;
  return table;
}

export function licenseInfo(id) {
  const t = licenseTable();
  return t[id] || t.unknown;
}

// Thứ tự quan trọng: AGPL, LGPL trước GPL; NC trước BY.
const RULES = [
  ["AGPL-3.0", /GNU AFFERO GENERAL PUBLIC LICENSE|\bAGPL-?3/i],
  ["LGPL-3.0", /GNU LESSER GENERAL PUBLIC LICENSE[\s\S]{0,200}Version 3|\bLGPL-?3/i],
  ["LGPL-2.1", /GNU LESSER GENERAL PUBLIC LICENSE|GNU LIBRARY GENERAL PUBLIC LICENSE|\bLGPL-?2/i],
  ["GPL-3.0", /GNU GENERAL PUBLIC LICENSE[\s\S]{0,200}Version 3|\bGPL-?3(\.0)?\b|GPLv3/i],
  ["GPL-2.0", /GNU GENERAL PUBLIC LICENSE|\bGPL-?2(\.0)?\b|GPLv2/i],
  ["MPL-2.0", /Mozilla Public License,? (Version|v\.?) ?2\.0|\bMPL-?2/i],
  ["OFL-1.1", /SIL OPEN FONT LICEN[CS]E|scripts\.sil\.org\/OFL|openfontlicense\.org|\bOFL\b/i],
  ["UFL-1.0", /UBUNTU FONT LICEN[CS]E/i],
  ["Apache-2.0", /Apache License,?\s*Version 2\.0|apache\.org\/licenses\/LICENSE-2\.0|\bApache-2\.0\b/i],
  ["CC0-1.0", /\bCC0\b|Creative Commons Zero|CC-ZERO|creativecommons\.org\/publicdomain\/zero/i],
  ["CC-BY-NC-ND-4.0", /Attribution-NonCommercial-NoDerivatives|\bCC[- ]BY[- ]NC[- ]ND\b|licenses\/by-nc-nd\//i],
  ["CC-BY-NC-SA-4.0", /Attribution-NonCommercial-ShareAlike|\bCC[- ]BY[- ]NC[- ]SA\b|licenses\/by-nc-sa\//i],
  ["CC-BY-NC-4.0", /Attribution-NonCommercial|\bCC[- ]BY[- ]NC\b|licenses\/by-nc\//i],
  ["CC-BY-ND-4.0", /Attribution-NoDerivatives|\bCC[- ]BY[- ]ND\b|licenses\/by-nd\//i],
  ["CC-BY-SA-4.0", /Attribution-ShareAlike|\bCC[- ]BY[- ]SA\b|licenses\/by-sa\//i],
  ["CC-BY-3.0", /Attribution 3\.0|\bCC[- ]BY[- ]3\.0\b|licenses\/by\/3\.0/i],
  ["CC-BY-4.0", /Creative Commons Attribution|\bCC[- ]BY\b|licenses\/by\/4\.0/i],
  ["Unlicense", /This is free and unencumbered software released into the public domain|\bUnlicense\b/i],
  ["MIT", /Permission is hereby granted, free of charge|\bMIT License\b|\bLicense: MIT\b|"license":\s*"MIT"/i],
  ["BSD-3-Clause", /Redistribution and use in source and binary forms[\s\S]*Neither the name/i],
  ["BSD-2-Clause", /Redistribution and use in source and binary forms/i],
  ["Zlib", /This software is provided ['‘]as-is['’], without any express or implied[\s\S]*plainly marked/i],
  ["ISC", /Permission to use, copy, modify, and\/or distribute this software for any purpose with or without fee/i],
  ["unity-companion", /Unity Companion License/i],
  ["spine-runtimes", /Spine Runtimes License/i],
  ["unity-asset-store-eula", /Asset Store (Terms of Service and )?(EULA|End User License)|unity\.com\/legal\/as-terms|unity3d\.com\/legal\/as_terms/i],
];

/** id license nhận ra trong một đoạn text, hoặc null. */
export function detectLicense(text) {
  if (!text) return null;
  const head = text.slice(0, 20000);
  for (const [id, re] of RULES) if (re.test(head)) return id;
  return null;
}

/** Tên file trông như file license / notice / credit. */
export const LICENSE_FILE = /^(licen[cs]e|copying|copyright|eula|notice|third[-_ ]?party[-_ ]?notices?|credits?|attribution|ofl)([-_. ].*)?(\.(txt|md|html?|rtf))?$|[-_ ](licen[cs]e|ofl|eula)\.(txt|md)$/i;
