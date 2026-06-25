"""
Batch add localization entries to the [Project Name] Google Sheet.
Writes key/en/vi directly + GOOGLETRANSLATE formulas for the other languages.
Does NOT clone the previous row.

Sheet layout (per tab) — column order comes from LocalizeDownloader.asset `codeList`:
  A=key  B=en  C=vi  D=pt  E=id  F=ru  G=th  H=es  I=ko  J=ja
  K=zhcn  L=zhtw  M=fr  N=de  O=it  P=pl  Q=nl  R=tr
English (column B) is the source for every GOOGLETRANSLATE formula.

Text columns (A=key, B=en, C=vi) are written with RAW so a value starting with
+, -, = is not parsed as a formula by Google Sheets.
Formula columns (D-R) are written with USER_ENTERED so they are evaluated.

Auth: a Google service-account JSON with edit access to the sheet.
  Path resolves from --creds, then $LOCALIZE_SA_FILE, then .agents/docs/service-account.json
  Share the spreadsheet with the service-account email (client_email in the JSON).

Usage:
  python .agents/scripts/batch_add_localize.py --file entries.json [--sheet shop]

JSON format (key + en required; vi and any language are optional overrides):
  [
    {"key": "ui_play",  "en": "Play",  "vi": "Choi"},
    {"key": "ui_pause", "en": "Pause", "vi": "Tam dung", "ja": "manual override"}
  ]
"""
import argparse
import json
import os
import sys

from google.oauth2 import service_account
from googleapiclient.discovery import build

sys.stdout.reconfigure(encoding='utf-8')

SPREADSHEET_ID = '1JDChbnV93bYxYP7ulX4X6KYZk9XAS4kHQDihaEnD-3c'
SCOPES = ['https://www.googleapis.com/auth/spreadsheets']
DEFAULT_SHEET = 'common'

# Column index (0-based) -> (JSON key, GOOGLETRANSLATE lang code).
# Columns A=key, B=en, C=vi are handled separately; these are the formula columns D-R.
LANG_COLS = [
    (3,  'pt',   'pt'),     # D - Portuguese
    (4,  'id',   'id'),     # E - Indonesian
    (5,  'ru',   'ru'),     # F - Russian
    (6,  'th',   'th'),     # G - Thai
    (7,  'es',   'es'),     # H - Spanish
    (8,  'ko',   'ko'),     # I - Korean
    (9,  'ja',   'ja'),     # J - Japanese
    (10, 'zhcn', 'zh-cn'),  # K - Chinese (Simplified)
    (11, 'zhtw', 'zh-tw'),  # L - Chinese (Traditional)
    (12, 'fr',   'fr'),     # M - French
    (13, 'de',   'de'),     # N - German
    (14, 'it',   'it'),     # O - Italian
    (15, 'pl',   'pl'),     # P - Polish
    (16, 'nl',   'nl'),     # Q - Dutch
    (17, 'tr',   'tr'),     # R - Turkish
]


def resolve_creds_path(cli_value):
    path = cli_value or os.environ.get('LOCALIZE_SA_FILE') \
        or os.path.join('.agents', 'docs', 'service-account.json')
    if not os.path.isfile(path):
        sys.exit(
            f"Service-account file not found: {path}\n"
            "Provide one with --creds, set $LOCALIZE_SA_FILE, or place it at "
            ".agents/docs/service-account.json, then share the sheet with its client_email."
        )
    return path


def resolve_sheet(spreadsheet, name):
    for s in spreadsheet['sheets']:
        if s['properties']['title'] == name:
            return s['properties']
    titles = ', '.join(s['properties']['title'] for s in spreadsheet['sheets'])
    sys.exit(f"Sheet '{name}' not found. Available tabs: {titles}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--file', required=True, help='Path to JSON file with entries')
    parser.add_argument('--sheet', default=DEFAULT_SHEET, help=f'Tab name (default: {DEFAULT_SHEET})')
    parser.add_argument('--creds', default='', help='Path to the Google service-account JSON')
    args = parser.parse_args()

    with open(args.file, encoding='utf-8') as f:
        entries = json.load(f)
    n = len(entries)
    print(f"Loaded {n} entries.")

    creds = service_account.Credentials.from_service_account_file(
        resolve_creds_path(args.creds), scopes=SCOPES)
    service = build('sheets', 'v4', credentials=creds)

    spreadsheet = service.spreadsheets().get(spreadsheetId=SPREADSHEET_ID).execute()
    props = resolve_sheet(spreadsheet, args.sheet)
    sheet_id = props['sheetId']
    sheet_name = props['title']
    current_row_count = props['gridProperties'].get('rowCount', 1000)

    col_a = service.spreadsheets().values().get(
        spreadsheetId=SPREADSHEET_ID, range=f"'{sheet_name}'!A:A"
    ).execute()
    start_row = len(col_a.get('values', [])) + 1
    end_row = start_row + n - 1
    print(f"Will write rows {start_row}-{end_row} of tab '{sheet_name}'.")

    if end_row > current_row_count:
        service.spreadsheets().batchUpdate(
            spreadsheetId=SPREADSHEET_ID,
            body={'requests': [{'appendDimension': {
                'sheetId': sheet_id,
                'dimension': 'ROWS',
                'length': end_row - current_row_count + 50,
            }}]}
        ).execute()
        print(f"Expanded grid by {end_row - current_row_count + 50} rows.")

    keys = []
    en_vi_pairs = []
    trans_formula_rows = []

    for i, entry in enumerate(entries):
        row_num = start_row + i
        key = entry.get('key', '').strip()

        keys.append([key])
        en_vi_pairs.append([entry.get('en', ''), entry.get('vi', '')])

        row_trans = []
        for _, json_key, lang_code in LANG_COLS:
            manual = entry.get(json_key, '')
            row_trans.append(manual if manual else f'=GOOGLETRANSLATE($B{row_num}, "en", "{lang_code}")')
        trans_formula_rows.append(row_trans)

    # RAW for text columns — prevents +/-/= prefix being parsed as a formula
    service.spreadsheets().values().batchUpdate(
        spreadsheetId=SPREADSHEET_ID,
        body={
            'valueInputOption': 'RAW',
            'data': [
                {'range': f"'{sheet_name}'!A{start_row}:A{end_row}", 'values': keys},
                {'range': f"'{sheet_name}'!B{start_row}:C{end_row}", 'values': en_vi_pairs},
            ],
        }
    ).execute()

    # USER_ENTERED for formula columns — must be evaluated by Sheets
    service.spreadsheets().values().batchUpdate(
        spreadsheetId=SPREADSHEET_ID,
        body={
            'valueInputOption': 'USER_ENTERED',
            'data': [
                {'range': f"'{sheet_name}'!D{start_row}:R{end_row}", 'values': trans_formula_rows},
            ],
        }
    ).execute()

    print(f"Wrote {n} rows. Done!")


if __name__ == '__main__':
    main()
