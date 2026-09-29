# Chạy EZG Auto Test System trên CI

Hướng dẫn chạy batchmode trên GitLab CI (runner macOS có Unity), đọc exit code, lịch chạy đề xuất và ghi chú
Firebase Test Lab.

## 1. Lệnh CLI

```bash
"$UNITY" -batchmode -projectPath "$PROJECT" \
  -executeMethod Ezg.AutoTest.Editor.AutoTestCli.Run \
  -autotestSuites static,smoke,ui-audit,economy \
  -autotestOut "$PROJECT/AutoTestReports/ci-$CI_PIPELINE_ID" \
  -autotestFailOn major \
  -logFile -
```

| Tham số | Ý nghĩa |
|---------|---------|
| `-executeMethod Ezg.AutoTest.Editor.AutoTestCli.Run` | Điểm vào CLI. |
| `-autotestSuites <id,id,…>` | Id suite cần chạy: `static`, `smoke`, `ui-audit`, `button-sweep`, `monkey`, `economy`, `performance`, `visual`, `device`, `scenarios`. |
| `-autotestOut <thư mục>` | Nơi ghi report (nên là đường dẫn tuyệt đối trong workspace để làm artifact). |
| `-autotestFailOn <severity>` | Ngưỡng làm job fail: `blocker`, `critical`, `major`, `minor`. Có issue ở mức này trở lên ⇒ exit `2`. |
| `-logFile -` | Log Unity ra stdout (xem được trong job log). |

- **Không truyền `-quit`.** Suite Play mode chạy bất đồng bộ qua nhiều frame; CLI tự gọi thoát Editor với đúng exit
  code khi lượt chạy xong. Có `-quit` Editor sẽ thoát ngay sau khi gọi hàm, trước khi test kịp chạy.
- **`-nographics`**: chỉ dùng khi chạy **riêng `static`** (nhanh hơn, không cần GPU). Suite Play mode cần graphics
  để chụp màn hình, UI audit (layout/raycast theo màn hình) và visual — không truyền `-nographics` cho chúng.
- Cấu hình dùng `ProjectSettings/EZGAutoTestSettings.json` đã commit — CI chạy đúng cấu hình team đang dùng.
- Mỗi project chỉ một Editor mở được tại một thời điểm: runner CI phải dùng checkout riêng (không phải thư mục dev
  đang mở Unity).

## 2. Exit code

| Code | Nghĩa | Gợi ý xử lý trên CI |
|------|-------|---------------------|
| `0` | Đạt — không có issue ≥ ngưỡng `-autotestFailOn`. | Job xanh. |
| `2` | Có lỗi test ≥ ngưỡng (lỗi của **game**). | Job đỏ; xem report artifact. Job nightly có thể cho `allow_failure: exit_codes: [2]` để hiện vàng thay vì chặn pipeline. |
| `3` | Lỗi runner / quá thời gian tổng (Editor treo, không vào được Play, license…). | Lỗi **hạ tầng** — xem job log, thường chạy lại được. |
| `4` | Tham số sai (id suite không tồn tại, thiếu `-autotestOut`…). | Sửa lệnh trong `.gitlab-ci.yml`. |

Code khác (vd `1`) đến từ chính Unity (không mở được project, lỗi compile) — xem log.

## 3. Job GitLab CI mẫu

```yaml
stages:
  - test

variables:
  GIT_DEPTH: "20"
  AUTOTEST_FAIL_ON: "major"

.autotest_base:
  stage: test
  tags: [macos, unity]            # runner Mac đã cài Unity đúng version + đã activate license
  timeout: 90 minutes
  before_script:
    - UNITY_VERSION=$(grep 'm_EditorVersion:' ProjectSettings/ProjectVersion.txt | awk '{print $2}')
    - export UNITY="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
    - export AUTOTEST_OUT="$CI_PROJECT_DIR/AutoTestReports/ci-$CI_PIPELINE_ID-$CI_JOB_NAME_SLUG"
  script:
    - |
      "$UNITY" -batchmode -projectPath "$CI_PROJECT_DIR" \
        $UNITY_EXTRA_ARGS \
        -executeMethod Ezg.AutoTest.Editor.AutoTestCli.Run \
        -autotestSuites "$AUTOTEST_SUITES" \
        -autotestOut "$AUTOTEST_OUT" \
        -autotestFailOn "$AUTOTEST_FAIL_ON" \
        -logFile -
  after_script:
    # Tóm tắt ngắn ngay trong job log.
    - cat AutoTestReports/ci-*/summary.md 2>/dev/null || true
  artifacts:
    when: always                   # giữ report cả khi job fail
    expire_in: 14 days
    paths:
      - AutoTestReports/**
    reports:
      junit: AutoTestReports/**/junit.xml

# Mỗi merge request: nhanh (~10–20 phút).
autotest:static:
  extends: .autotest_base
  variables:
    AUTOTEST_SUITES: "static"
    UNITY_EXTRA_ARGS: "-nographics"          # chỉ static mới được -nographics
  rules:
    - if: $CI_PIPELINE_SOURCE == "merge_request_event"

autotest:mr:
  extends: .autotest_base
  variables:
    AUTOTEST_SUITES: "smoke,ui-audit,economy"
  rules:
    - if: $CI_PIPELINE_SOURCE == "merge_request_event"

# Hằng đêm: đầy đủ suite trong Editor.
autotest:nightly:
  extends: .autotest_base
  timeout: 3 hours
  variables:
    AUTOTEST_SUITES: "static,smoke,ui-audit,button-sweep,monkey,economy,performance,visual,scenarios"
  allow_failure:
    exit_codes: [2]                          # lỗi game ⇒ vàng + report; lỗi hạ tầng (3/4) vẫn đỏ
  rules:
    - if: $CI_PIPELINE_SOURCE == "schedule" && $AUTOTEST_SCHEDULE == "nightly"

# Hằng tuần / trước release: chạy trên device thật cắm vào runner.
autotest:device:
  extends: .autotest_base
  tags: [macos, unity, android-device]
  timeout: 3 hours
  variables:
    AUTOTEST_SUITES: "device"
  rules:
    - if: $CI_PIPELINE_SOURCE == "schedule" && $AUTOTEST_SCHEDULE == "weekly"
    - if: $CI_COMMIT_BRANCH =~ /^release\//
      when: manual
```

Ghi chú:

- `junit.xml` giúp GitLab hiện số test đạt/lỗi trong tab *Tests* của pipeline và widget MR.
- Report HTML xem trực tiếp trong trình duyệt artifact của GitLab (*Browse* → `report.html`).
- Đường dẫn Unity trên runner khác Hub mặc định ⇒ đặt biến CI/CD `UNITY` và bỏ dòng `export UNITY` trong `before_script`.
- License: runner phải activate Unity sẵn (serial Pro/Plus hoặc floating license server). Không đưa username/password
  Unity vào `.gitlab-ci.yml`; nếu buộc phải truyền thì dùng biến CI/CD dạng masked + protected.
- Muốn so sánh issue "MỚI" giữa các lượt nightly trên CI: giữ lại thư mục `AutoTestReports/` giữa các job (cache theo
  branch) — nếu không, mỗi job chỉ thấy lượt của chính nó.
- `AutoTestReports/` nên nằm trong `.gitignore`; `AutoTestBaselines/` thì **commit**.

## 4. Lịch chạy đề xuất

| Khi nào | Suite | Ngưỡng | Mục tiêu |
|---------|-------|--------|----------|
| Mỗi MR | `static` (có `-nographics`) + `smoke,ui-audit,economy` | `major` | Chặn lỗi mới trước khi merge. |
| Hằng đêm (schedule, biến `AUTOTEST_SCHEDULE=nightly`) | Toàn bộ suite Editor: + `button-sweep,monkey,performance,visual,scenarios` | `major`, `allow_failure` exit 2 | Bắt lỗi sâu, regression hiệu năng, thay đổi hình ảnh. QA đọc sáng hôm sau. |
| Hằng tuần + trước release (biến `AUTOTEST_SCHEDULE=weekly`, nhánh `release/*`) | `device` (chạy `device.suites` trên máy thật) | `major` | Hiệu năng tuyệt đối, cold start, logcat, hành vi trên phần cứng thật. |

Tạo schedule: GitLab > Build > Pipeline schedules → thêm biến `AUTOTEST_SCHEDULE` = `nightly` / `weekly`.

## 5. Visual Regression trên CI

- Baseline phụ thuộc độ phân giải + GPU: chạy `visual` trên **một runner cố định** cùng độ phân giải Game view mà
  baseline được tạo. Máy khác ⇒ lệch khử răng cưa ⇒ nới `visual.pixelTolerance` hoặc tạo baseline riêng.
- Runner headless (không có màn hình / session đồ hoạ) có thể cho ảnh đen ⇒ chạy `visual` trên runner có session
  đồ hoạ (Mac đăng nhập sẵn user), hoặc chỉ chạy trong Editor khi review.
- Không chấp nhận baseline trên CI. Người review tải report, xem diff, rồi chấp nhận baseline trong Editor local và
  commit.

## 6. Device trên CI

- Runner cần Android SDK (adb) — dùng SDK Unity cài kèm, hoặc đặt `device.adbPath` trong Settings.
- Cắm máy vào runner, bật USB debugging, tin cậy máy tính; kiểm tra `adb devices` trong job trước khi chạy.
- Build test tự thêm define `EZG_AUTOTEST` chỉ cho build đó (`extraScriptingDefines`) — không đụng Player Settings,
  build release song song không bị ảnh hưởng. Suite `static` còn kiểm tra `EZG_AUTOTEST` không nằm trong define
  release.
- Nhiều máy: chạy nhiều job với tag runner khác nhau, mỗi runner một máy.

## 7. Firebase Test Lab (Game Loop)

Firebase Test Lab chạy APK trên máy thật của Google theo chế độ **Game Loop**: khởi động app bằng intent
`com.google.intent.action.TEST_LOOP`, app tự chạy rồi tự kết thúc. Package hỗ trợ sẵn:

- **APK test** do suite `device` build (define `EZG_AUTOTEST`, trong `device.buildOutputFolder`) đã được chèn
  intent-filter `TEST_LOOP` vào Activity khởi động — **chỉ** trong build test, build release không bị đụng.
- Khi được mở bằng intent Game Loop, runner trong app tự chạy các suite ở **Settings > Device > suites**
  (`device.suites`), ghi kết quả tóm tắt (runId, scenario, device, `summary` gồm số case theo trạng thái +
  `healthScore`, danh sách case Lỗi/Crash) vào file kết quả Game Loop mà Test Lab truyền qua intent, rồi tự đóng game.
- `report.json` + ảnh chụp đầy đủ nằm ở `Application.persistentDataPath/EZGAutoTest/<runId>/` — trên Android là
  `/sdcard/Android/data/<package>/files/EZGAutoTest/`, kéo về bằng `--directories-to-pull`.
- Case **firebase-test-lab** của suite `device` sinh sẵn lệnh `gcloud` (file đính kèm `ftl-command.txt`, model /
  API lấy theo máy đang cắm). Lệnh không tự chạy — copy ra Terminal hoặc vào job CI.

Chuẩn bị một lần: cài Google Cloud SDK, `gcloud auth login` (trên CI: service account
`gcloud auth activate-service-account --key-file=…` với key lưu trong biến CI/CD masked), `gcloud config set project
<firebase-project-id>`. Danh sách máy: `gcloud firebase test android models list`.

```bash
gcloud firebase test android run \
  --type game-loop \
  --app Builds/AutoTest/<tên-apk>.apk \
  --scenario-numbers 1 \
  --device model=<mã-máy>,version=<api>,locale=vi,orientation=portrait \
  --timeout 15m \
  --results-dir ezg-autotest-<runId> \
  --directories-to-pull /sdcard/Android/data/<package>/files/EZGAutoTest
```

Xem kết quả: Firebase Console > Test Lab > lượt chạy > tab *Game loop results*, hoặc thư mục kết quả trên GCS
(tải thư mục `EZGAutoTest/<runId>/` về để mở report). Thêm nhiều `--device` để chạy song song nhiều model.

Game Loop hợp cho smoke / economy / kịch bản riêng trên nhiều model máy; performance trên Test Lab chỉ nên so
tương đối giữa các lần chạy cùng model.
