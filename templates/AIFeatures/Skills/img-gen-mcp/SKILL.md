---
name: img-gen-mcp
description: "Use when: generate images, storyboard, ability concept art, NPC portraits, UI atlas/screens, isometric room assets, app-store screenshots, pose change, style sync, upscale, describe image, asset generation, img-gen MCP, batch image generation, style analysis, video generation, Gemini Omni, Veo, Seedance, text-to-video, image-to-video, real face, portrait asset, PrivacyInformation, sprite animation, spritesheet, Ludo, motion transfer, sprite pose, rotate sprite, new direction, Hydra+ legacy, improve details, normal map, create normal map, directional sprites, 3D model, GLB, rig, 8 directions, weapons, accessories, sword, shield, helmet, text-to-motion animation, Tripo, 3D model from text, image to 3D, multiview, Gaussian splat, auto rig, retarget animation, FBX, USDZ, convert 3D format, retopology, segment mesh, img-gen link, deep link, deeplink, open_link, read a shared img-gen URL"
---

# img-gen MCP

Remote MCP server for automated image/asset generation, using the same apps as the img-gen web UI:
Image Generator, Story Board Gen, Ability Concept Art, M1 NPC Generator, Screenshot App,
Iso Room Gen, UI Generator, Creative Studio, Pose Editor, Style Extractor (describe_image),
Sprite Upscale (upscale_image), Sync Style, Video Gen (generate_video: Gemini Omni Flash, Veo and Seedance;
add_portrait_asset / get_portrait_asset: real faces in Seedance), Sprite Gen (Ludo.ai: generate_sprite, transfer_sprite_motion,
edit_sprite, adjust_sprite, sprite_new_direction, add_sprite_sound, generate_sprite_pose, rotate_sprite, create_normal_map, list_sprite_presets), Directional Sprites (Ludo.ai: create_character_image, create_3d_model,
rig_3d_model, create_animation, animate_3d_model, create_directional_sprites, create_directional_stills, list_directional_styles,
list_my_animations, preview_directional_sprites, fit_accessory, attach_accessories: 3D characters with weapons and accessories, up to 8 directions),
Tripo 3D (tripo_generate_model, tripo_image_to_splat, tripo_generate_image, tripo_process_model, list_tripo_options: 3D models, rigging, animation, format conversion)
and the VFX Library (search_vfx, get_vfx: texture search).

## Connection

### Claude Code (one-line installer, user scope — recommended)

macOS, OAuth (browser Google login):

```bash
bash <(curl -fsSL https://img-gen.easygoing.vn/installers/img-gen-mcp-setup.command)
```

Windows: https://img-gen.easygoing.vn/technical-art-tools?tool=api-access → Connect Claude Code → Windows toggle (PowerShell one-liner).
Uninstall: same card → Uninstall toggle.

The installer removes any existing `img-gen` server from local/project/user scope, re-adds it at
**user scope** so it is available in every project, logs in, and verifies with `whoami`.

### Claude Code (manual)

```bash
claude mcp add --transport http --scope user img-gen https://img-gen.easygoing.vn/api/mcp
claude mcp login img-gen
```

### Claude Desktop / claude.ai

Settings → Connectors → Add custom connector → URL \`https://img-gen.easygoing.vn/api/mcp\` → Connect → Google login → Allow.

## Tools

### whoami

No input. Returns `{ email, isAdmin, auth, scopes, apps, server }` for the signed-in account.
Instant and side-effect free — call it first to check the connection.

### list_capabilities

No input. Returns available apps, models, defaults, aspect ratios, resolutions,
blocked params, GPT param enums, and limits.

### upload_reference_image

Upload a reference image before passing it to a generation tool.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| base64 | string | one of base64/sourceUrl | Raw base64, ≤ 4 MB decoded |
| sourceUrl | string | one of base64/sourceUrl | HTTPS URL; prefer this or existing file URLs |
| mimeType | string | no | Sniffed if omitted |
| name | string | no | Filename hint |

Returns \`{ url, mimeType, bytes }\`. Use the returned \`url\` in generation tools.

### generate_images

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| prompt | string | — | Required |
| referenceImageUrls | string[] | [] | ≤ 10, must be file URLs (upload first) |
| structureImageUrls | string[] | [] | ≤ 5, must be file URLs |
| model | string | gemini-3.1-flash-lite-image | See model list |
| aspectRatio | string | 1:1 | See aspect ratios |
| resolution | string | 1K | 1K, 2K, or 4K (lite model = 1K only) |
| quantity | number | 1 | 1–8 |
| useSystemPrompt | boolean | false | Use the built-in image gen prompt |
| gpt | object | — | GPT-only params when using gpt-image-2.5-sunburst |

Returns \`{ batchId, items: [{ key, label, jobId }] }\`.

### split_story_into_scenes

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| masterDesc | string | — | Required |
| sceneCount | number | — | Optional target count |
| textModel | string | gemini-3.5-flash | Text model |

Returns \`{ scenes: string[] }\`. Synchronous.

### analyze_style_reference

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| imageUrl | string | — | Required, must be a file URL |
| textModel | string | gemini-3.5-flash | Text model |

Returns \`{ styleDescription }\`. Synchronous.

### generate_storyboard

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| scenes | string[] | — | Required, 1–12 scene descriptions |
| styleDescription | string | — | Optional style text |
| styleReferenceUrls | string[] | [] | file URLs |
| materialReferenceUrls | string[] | [] | file URLs |
| model | string | gemini-3.1-flash-lite-image | Image model |
| aspectRatio | string | 16:9 | Aspect ratio |
| resolution | string | 1K | Resolution |
| quantity | number | 1 | Variants per scene, 1–4 |
| useSystemPrompt | boolean | false | |
| gpt | object | — | GPT-only params |

Returns \`{ batchId, items }\`.

### generate_ability_concepts

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| characterImageUrl | string | — | Required, file URL of the character |
| skills | array | — | Required, 1–8 objects with name, description, intensity (1–10) |
| useSystemPrompt | boolean | false | Generate AI prompt per skill |
| useCharacterAsRef | boolean | true | Include character as image reference |
| model | string | gemini-3.1-flash-lite-image | Image model |
| textModel | string | gemini-3.5-flash | For prompt generation |
| aspectRatio | string | 1:1 | |
| resolution | string | 1K | |
| quantity | number | 1 | Variants per skill, 1–4 |
| gpt | object | — | GPT-only params |

Returns \`{ batchId, items }\`.

### generate_pose_change

Pose Editor: redraws the character in the exact pose from a second image. Identity, outfit and art style stay the same.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| characterImageUrl | string | — | Required. file URL of the character to recreate (Image 1) |
| poseImageUrl | string | — | Required. file URL of the target pose (Image 2) |
| instructions | string | — | Optional extra instructions, added to the end of the pose prompt |
| model | string | gemini-3.1-flash-lite-image | See model list |
| aspectRatio | string | 1:1 | See aspect ratios |
| resolution | string | 1K | 1K, 2K, or 4K (lite model = 1K only) |
| quantity | number | 1 | 1–8. Item keys are pose, or pose-v1..pose-vN when quantity > 1 |
| gpt | object | — | GPT-only params when using gpt-image-2.5-sunburst |

Returns `{ batchId, items: [{ key, label, jobId }], next }`. Poll with get_job or wait_for_job.

### generate_studio_image

Creative Studio image mode: generates an image from a prompt, with optional material, layout and style reference images and a camera angle.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| prompt | string | — | Required, not blank. Image description |
| materialImageUrls | string[] | [] | ≤ 10 file URLs. The exact subject, character or object that must appear |
| layoutImageUrls | string[] | [] | ≤ 10 file URLs. Composition: camera angle, placement, framing, depth |
| styleImageUrls | string[] | [] | ≤ 10 file URLs. Art style to copy: palette, lighting, line work, rendering |
| cameraAngle | string | — | One of: Full Body Shot, Close-up Shot, Extreme Close-up Shot, Extreme Wide Shot, High Angle Shot, Bird's Eye View Shot, Dutch Angle Shot, Side Profile Shot, Low Angle Shot, Over-the-Shoulder Shot, Off-Center Shot, Shot From Behind. Leave it out for no angle |
| model | string | gemini-3.1-flash-lite-image | See model list |
| aspectRatio | string | 1:1 | See aspect ratios |
| resolution | string | 1K | 1K, 2K, or 4K (lite model = 1K only) |
| quantity | number | 1 | 1–8. One job per image, keyed image-1..image-N |
| gpt | object | — | GPT-only params when using gpt-image-2.5-sunburst |

At most 16 reference images across all three lists.

Returns `{ batchId, items: [{ key, label, jobId }], next }`. Poll with get_job or wait_for_job.

### upscale_image

Content-preserving re-generation of 1-16 images at a higher resolution, one job per image (Creative Studio Upscale / Sprite Upscale).

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| imageUrls | string[] | — | Required, 1–16 file URLs (upload_reference_image, create_upload_url, or earlier results) |
| preset | string | content | `content`: strict pixel-faithful upscale (Creative Studio). `sprite`: HD upscale that also removes pixelation/JPEG artifacts (Sprite Upscale) |
| prompt | string | — | Replaces the preset instruction text |
| resolution | string | 2K | 1K, 2K, or 4K. gemini-3.1-flash-lite-image supports only 1K (2K/4K are rejected, not clamped) |
| aspectRatio | string | — | Omit (or `auto`) to keep the input shape. Extended ratios (1:4, 1:8, 4:1, 8:1) need gemini-3.1-flash-image or gemini-nano-banana-2.1 |
| model | string | gemini-3.1-flash-image | Gemini image models only; GPT is rejected |

Returns `{ batchId, items: [{ key, label, jobId, meta: { sourceUrl, assetPrompt, variant } }], next }`. Poll with get_job / wait_for_job.

### describe_image

Describe or analyze image(s) with a text model, using the same prompts as the web apps. Synchronous.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| imageUrls | string[] | — | Required file URLs. Exactly 1 image, except `screenshot_style` (1–6) |
| mode | string | — | Required. `content`: physical content only, ignores style. `style_keywords`: comma-separated style keywords (Style Extractor). `style_brief`: production-ready style brief (Story Board Gen). `reverse_prompt`: sectioned style + content prompt to replicate the image (Creative Studio Analyze). `syncstyle_content`: Sync Style content description with view prefix. `syncstyle_style`: Sync Style style description. `screenshot_style`: reusable style guide from app-store screenshot references |
| viewMode | string | neutral | `syncstyle_content` only: platformer, neutral, or isometric. platformer/isometric prepend a view prefix and tell the model to omit perspective |
| textModel | string | gemini-3.5-flash | See text model list |

Returns `{ mode, text }`. For `syncstyle_content`, `text` includes the view prefix (same text the web puts in the content box).

### generate_npc_main_texts

M1 NPC Generator step 1: writes MainText variants from character reference images. Each MainText is an image-generation prompt for a new NPC who is the same age but looks different. The call returns when all texts are ready (synchronous).

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| referenceImageUrls | string[] | — | Required, 1–10 file URLs of the original character. Used only for this text step |
| extraDescription | string | — | Optional short direction: age, personality, job, outfit (e.g. "She is flower shop's owner.") |
| count | number | 1 | Number of MainText variants, 1–8. One text call per variant, run in parallel |
| textModel | string | gemini-3.5-flash | Text model |

Returns `{ mainTexts: string[] }`. Synchronous. Edit the texts if needed, then pass them to generate_npc_images.

### generate_npc_images

M1 NPC Generator step 2: renders NPC portraits, one per MainText × quantity, styled by optional art-style reference images.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| mainTexts | string[] | — | Required, 1–8 MainTexts (usually from generate_npc_main_texts). A blank one falls back to a generic young NPC |
| imageReferenceUrls | string[] | [] | Up to 10 file URLs of NPC art-style references, sent with every prompt |
| promptTemplate | string | built-in NPC portrait template | Must contain `[MainText]`, which is replaced by each MainText |
| model | string | gemini-3.1-flash-lite-image | Image model |
| aspectRatio | string | 1:1 | Aspect ratio |
| resolution | string | 1K | 1K, 2K, or 4K (lite model = 1K only) |
| quantity | number | 1 | Images per MainText, 1–8. mainTexts × quantity must be ≤ 16 |
| gpt | object | — | GPT-only params |

Returns `{ batchId, items }`. Jobs run in the web's order: image 1 for every MainText, then image 2, and so on. Each item's meta has `mainText`, `mainTextIndex`, `variant` (the image index) and `assetPrompt` (the full prompt). Poll with get_job / wait_for_job.

### generate_ui_style_atlas

UI Generator stage 1: make a 1:1 game UI atlas sprite sheet (buttons in 3 states, containers, bars, typography) from aesthetic sliders or a custom prompt. Use the result URL as the style reference for generate_ui_screens.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| sliders | array | [] | Up to 10 `{ id, value }` items, each id at most once. id: realism, decoration, organic, wear, opacity, glow, corners, borders, saturation, skew. value 0–100. Only the sliders you pass apply; none = balanced standard game UI |
| prompt | string | — | Full custom atlas prompt that replaces the slider-built one. Don't combine with sliders |
| model | string | gemini-3.1-flash-lite-image | Image model |
| resolution | string | 1K | 1K, 2K, or 4K (lite model = 1K only) |
| quantity | number | 1 | Variants, 1–4 |
| gpt | object | — | GPT-only params |

Aspect ratio is always 1:1.

Returns `{ batchId, items }`.

### generate_ui_screens

UI Generator stage 2: make game UI screen mockups in the style of the reference image(s), such as the atlas URL or uploaded UI references.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| styleReferenceUrls | string[] | — | Required, 1–10 file URLs (atlas result or upload_reference_image) |
| screens | array | — | Required, 1–16 items. Each item needs a catalog `id`, a custom `name` + `description`, or a full `prompt` |
| screens[].id | string | — | main_menu, inventory, character, settings, shop, level_select, hud, victory, defeat, quest. Each id at most once |
| screens[].name | string | catalog name | Required for custom screens |
| screens[].description | string | catalog description | Screen contents/options |
| screens[].prompt | string | — | Full prompt override. "base on the style above," is added automatically |
| model | string | gemini-3.1-flash-lite-image | Image model |
| aspectRatio | string | 1:1 | Also sets the orientation hint (square / horizontal / vertical; none for auto) |
| resolution | string | 1K | Resolution |
| quantity | number | 1 | Variants per screen, 1–4 (screens × variants ≤ 16) |
| gpt | object | — | GPT-only params |

Returns `{ batchId, items }` (item keys are the screen id, or custom-N for custom screens).

### analyze_room

Iso Room Gen step 1. Describes the empty room and lists every furniture and decor item in a room image. Run it on the source photo before generate_iso_styler. Optionally run it again on the styler image before generate_iso_assets, as the web's Calculate Items does.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| imageUrl | string | — | Required. file URL |
| view | string | Isometric | `Isometric` or `Side scrolling`. Use the same view for every step |
| textModel | string | gemini-3.5-flash | Text model |

Returns `{ roomDescription, items: string[] }`. Synchronous.

### generate_iso_styler

Iso Room Gen step 2. Renders the source room photo as one master isometric or side-scrolling styler image. Later assets copy its style.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| sourceImageUrl | string | — | Required. file URL of the room photo |
| analysis | object | — | `{ roomDescription, items[] }` from analyze_room. Required unless prompt is set |
| prompt | string | built from analysis | Overrides the styler prompt |
| view | string | Isometric | `Isometric` or `Side scrolling` |
| model | string | gemini-3.1-flash-lite-image | Image model |
| aspectRatio | string | 1:1 | Aspect ratio |
| resolution | string | 4K | Resolution. The lite model clamps it to 1K |
| gpt | object | — | GPT-only params |

Returns `{ batchId, items }` with one `styler` item. Pass its image URL as referenceUrl to generate_iso_assets.

### generate_iso_assets

Iso Room Gen step 3. Generates the empty room base and the item sprites in the style of the reference, either one image per item or packed into sprite-sheet grids.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| referenceUrl | string | — | Required. file URL of the styler (preferred) or of the source photo |
| analysis | object | analyze referenceUrl | `{ roomDescription, items[] }`. Edit items to add, rename or drop assets |
| view | string | Isometric | `Isometric` or `Side scrolling` |
| gridLayout | string | 2x2 grid | `1 item`, `2x2 grid`, `3x3 grid` or `4x4 grid` (1, 4, 9 or 16 items per image) |
| includeRoom | boolean | true | Also generate the empty room base |
| model | string | gemini-3.1-flash-lite-image | Image model |
| textModel | string | gemini-3.5-flash | Used only when analysis is omitted |
| roomAspectRatio | string | 1:1 | Room image ratio |
| roomResolution | string | 2K | Room image resolution. The lite model clamps it to 1K |
| itemAspectRatio | string | 1:1 | Item or grid ratio |
| itemResolution | string | 1K | Item or grid resolution |
| gpt | object | — | GPT-only params |

Each call is limited to 16 jobs: 1 for the room plus 1 per item or grid. For many items, use a bigger grid, or split the items across calls and set `includeRoom: false` on every call after the first. Item keys are `room`, `item-N` and `grid-N`. `meta.itemNames` lists each image's items. In a grid they are cells A, B, C… in that order.

Returns `{ batchId, items }`, plus `analysis` when it was computed from referenceUrl.

### generate_screenshot_set

Generate a cohesive app-store screenshot set (Screenshot App). Each slot is one panel, with `quantity` images per slot.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| slots | array | — | Required, 1–8 objects `{ mainText, description, layoutReferenceUrls, materialReferenceUrls }` (all optional) |
| slots[].mainText | string | "" | Headline text, kept exactly |
| slots[].description | string | "" | Scene direction for the panel |
| slots[].layoutReferenceUrls | string[] | [] | file URLs that set the composition of this panel only |
| slots[].materialReferenceUrls | string[] | [] | file URLs of product/UI/character details for this panel |
| styleReferenceUrls | string[] | [] | ≤ 10 file URLs that set typography, rendering and set identity |
| materialReferenceUrls | string[] | [] | ≤ 10 file URLs of details shared by every panel |
| styleAnalysis | string | auto | Style-lock text. Omit it to auto-analyze the first 6 style refs; pass "" to skip |
| textModel | string | gemini-3.5-flash | Used only for the auto style analysis |
| model | string | gemini-3.1-flash-lite-image | Image model |
| aspectRatio | string | 1:1 | |
| resolution | string | 1K | |
| quantity | number | 1 | Images per slot, 1–8. On Gemini, slots × quantity ≤ 16 |
| useSystemPrompt | boolean | false | |
| gpt | object | — | GPT-only params |

Each panel sends its first 10 references in this order: style refs, slot layout refs, slot material refs, global material refs.

Returns `{ batchId, items, styleAnalysis?, styleAnalysisError? }`. `styleAnalysis` is included only when it was auto-analyzed. Pass it back on later calls to skip analyzing again.

### generate_screenshot_variant

Generate alternate compositions of an existing screenshot that keep its headline text (Screenshot App variant).

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| sourceImageUrl | string | — | Required. file URL of the screenshot, e.g. a generate_screenshot_set result |
| mainText | string | "" | Headline text on the source, kept exactly |
| description | string | "" | Used only as the result title when mainText is empty |
| model | string | gemini-3.1-flash-lite-image | Image model |
| aspectRatio | string | 1:1 | Use the ratio the set was generated with |
| resolution | string | 1K | |
| quantity | number | 1 | Number of alternates, 1–8 |
| gpt | object | — | GPT-only params |

Returns `{ batchId, items }`.

### generate_syncstyle

Combine a content description with a style taken from reference images or from text (Sync Style).

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| contentPrompt | string | — | Required. What to show: subjects, layout, composition. No art style |
| viewMode | string | neutral | platformer, neutral or isometric. platformer and isometric add a sprite-sheet view prefix, so leave camera/view out of contentPrompt |
| styleMode | string | — | Required. `image` uses styleImageUrls, `text` uses stylePrompt |
| styleImageUrls | string[] | [] | ≤ 10 file URLs. Required for `image`, ignored for `text` |
| stylePrompt | string | — | How it should look (medium, palette, lighting, mood). Required for `text` |
| model | string | gemini-3.1-flash-lite-image | Image model |
| aspectRatio | string | 1:1 | |
| resolution | string | 1K | |
| quantity | number | 1 | Number of images, 1–8 |
| gpt | object | — | GPT-only params |

Returns `{ batchId, items }`.

### generate_video

Text-to-video, or image-to-video when `startFrameUrl` is set. Google (Gemini Omni Flash, Veo) and Seedance (BytePlus) models.
Google now answers 404 "no longer available" for the Veo 3.1 preview ids; they stay listed, but use `gemini-omni-1.1-flash` for Google video.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| prompt | string | — | Required, except with draftTaskId (the draft's prompt is reused). Seedance tags: 2.0 series @image1 / @video1 / @audio1, 2.5 @Image1 / @Video1 / @Audio1 |
| model | string | byteplus-seedance-2.0 | gemini-omni-1.1-flash, veo-3.1-generate-preview, veo-3.1-fast-generate-preview, veo-3.1-lite-generate-preview, byteplus-seedance-2.0, byteplus-seedance-2.0-fast, byteplus-seedance-2.0-mini, byteplus-seedance-2.5 |
| startFrameUrl | string | — | First frame, image file URL (upload first). Seedance also takes `asset://<id>` (see add_portrait_asset) |
| endFrameUrl | string | — | Omni and Seedance: last frame; needs startFrameUrl. Seedance also takes `asset://<id>` |
| referenceImageUrls | string[] | [] | Seedance: 2.0 series ≤ 9, 2.5 ≤ 30 image file URLs or `asset://<id>`. Omni: subject references, not with start/end frames, ≤ 3 images in all |
| referenceVideoUrls | string[] | [] | Seedance only: 2.0 series ≤ 3 (2–15 s each, ≤ 15 s total), 2.5 ≤ 10 (2–30 s each, ≤ 30 s total); mp4/mov/webm via `create_upload_url` |
| referenceAudioUrls | string[] | [] | Seedance only: mp3/wav via `create_upload_url`. 2.0 series ≤ 3 and only with an image or video reference; 2.5 ≤ 10, audio alone allowed |
| aspectRatio | string | 16:9 | Omni, Veo: 16:9, 9:16. Seedance: adaptive, 16:9, 9:16, 1:1, 4:3, 3:4, 21:9. Seedance 2.5 start/end frame clips and taskType edit / extend always run as adaptive (any other value is replaced; the result lists it in `notes`) |
| durationSeconds | number | Veo 8, Seedance 5 | Veo: 4, 6, 8. Seedance 2.0 / 2.0-fast / 2.0-mini: 4–15 or -1. Seedance 2.5: 4–30 or -1. -1 = the model picks the length. Rejected for Omni (the model picks 3–10 s) |
| resolution | string | Omni, Seedance 720p; Veo provider default | Omni: 360p, 720p, 1080p, 4k (1080p / 4k are upscaled). Veo: 720p, 1080p (1080p needs 8 s). Seedance 2.0: 480p, 720p, 1080p, 4k. 2.0-fast / 2.0-mini: 480p, 720p. 2.5: 480p, 720p, 1080p |
| negativePrompt | string | — | Veo only; rejected for Omni |
| generateAudio | boolean | false | Seedance only; rejected for Omni. Omni and Veo always generate audio |
| seed | number | — | Seedance only; rejected for Omni |
| returnLastFrame | boolean | false | Seedance: done items also carry `videos[].lastFrameUrl` (image of the last frame; use it as the next clip's startFrameUrl). BytePlus trusts this frame for real faces, like the clip |
| draft | boolean | false | Seedance 2.5 only: cheap 480p preview (resolution must be 480p or omitted). Keep its `videos[].arkTaskId` for the final |
| draftTaskId | string | — | Seedance 2.5 only: the `arkTaskId` of a done draft (valid 14 days). Makes the 1080p final; the draft's prompt, references, aspectRatio, durationSeconds, seed, generateAudio and taskType are reused and must be omitted. Allowed: model, resolution (1080p), outputFormat, returnLastFrame, quantity; a prompt only labels the history item |
| outputFormat | string | mp4 | Seedance 2.5 only: mp4 or mov (H.264 yuv444p + PCM audio, mimeType video/quicktime) |
| taskType | string | — | Seedance 2.5 only, with reference images/videos/audio (not frames): auto, reference, edit, extend. edit: ≥ 1 reference video 4–30 s, forces aspectRatio adaptive and durationSeconds -1; the prompt must ask to edit/add/remove/replace. extend: ≥ 1 reference video, forces aspectRatio adaptive; the prompt must ask to extend/continue |
| quantity | number | 1 | 1–4; each clip is a separate paid job |

Returns `{ batchId, items: [{ key, label, jobId }], notes? }` with keys `video-1`, `video-2`, …. `notes` lists each value that was replaced
and, for Seedance, each frame or reference image that will be blocked if it shows a real face (see Real faces in Seedance).
Done items carry `videos: [{ url, mimeType, posterUrl?, lastFrameUrl?, arkTaskId? }]` (`arkTaskId` on Seedance results). Clips take minutes: poll with `wait_for_job` repeatedly.
Counts against the shared daily video limit (see `list_capabilities` → `video`); a draft and its final are two clips.
Seedance 2.5 draft flow: `generate_video` with `draft: true` → check the 480p clip → `generate_video` with `model: "byteplus-seedance-2.5"` and `draftTaskId`.

### add_portrait_asset

Adds a face image to the BytePlus portrait library, so Seedance accepts its real face. Use it for a face image that BytePlus does not trust,
for example a Seedream image made with a reference image. See Real faces in Seedance.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| url | string | yes | img-gen file URL of the image (upload_reference_image, create_upload_url, or an earlier result) |

Returns the asset: `assetId`, `assetUrl` (`asset://<id>`), `status` and `error`. `status` is `Processing` while BytePlus reviews the image,
`Active` when it can be used, `Failed` when BytePlus refused it, and `error` when img-gen could not submit it. The same image added again
returns its existing asset, unless that one failed.

### get_portrait_asset

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| assetId | string | yes | From add_portrait_asset |

Asks BytePlus for the current status and returns the asset in the same shape. Call it until `status` is `Active`, then pass `assetUrl`
to generate_video (Seedance models only) as startFrameUrl, endFrameUrl or a referenceImageUrls item.

### Sprite Gen (Ludo.ai): shared rules

Every Sprite Gen operation of the web app, one tool each. All file inputs are img-gen file URLs (upload first), sent to Ludo as-is (never resized);
images must be image files, `videoUrl` a video file. Every tool returns `{ batchId, app: "sprite-gen", operation, model, items: [{ key, label, jobId }], quantity, variations?, hint }`;
poll with `wait_for_job` (`timeoutSec` 120, repeat while `pending`). A run usually takes 2–3 min but can take hours when Ludo is busy: the server keeps polling Ludo for 48 h and retries the file copies for 72 h, so items stay `pending` that long and every result lands in History. Keep polling; never resubmit a pending item. Items that never reached Ludo time out after 30 min.
All result files are copied to our storage: the URLs are permanent file URLs, never Ludo URLs. Non-admin results are posted to the team Discord automatically.
`list_capabilities.sprite` lists the operations with their tool, models, durations and credits, plus edit modes, poses, camera angles and preset views.
**Legacy models** (`list_capabilities.sprite.legacyModels`, `models[].legacy`): Ludo still runs them but will remove them. hydra-plus is legacy since
2026-11-02: griffin-hd took its high-detail role. forge and forge-pixel are legacy since 2026-11-17: ignis and ignis-pixel took over.
hydra stays the default. Do not pick a legacy model for new work.

**Sheet results** (generate_sprite, transfer_sprite_motion, edit_sprite, adjust_sprite, sprite_new_direction, add_sprite_sound): done items carry
`sprite: { spritesheet, video, spritesheetWithBackground?, gif?, audio?, normalMap?, frames?, framesWithBackground?, numFrames, numCols, numRows, duration }`
and `operation`; each file is `{ url, mimeType }` (frames are arrays). `images` holds the spritesheet (and the background sheet) and `videos` the video.
`normalMap` is a lossless WebP normal map (OpenGL convention: Unity and Godot read it as is, Unreal needs green flipped).

**Image results** (generate_sprite_pose, rotate_sprite, create_normal_map): done items carry `images: [{ url, mimeType, normalMap?, pose?, motionPrompt?, cameraRotation?, cameraElevation? }]`
and `operation`. `motionPrompt` is Ludo's suggested prompt for animating that pose with generate_sprite. For create_normal_map `url` is the source image and `normalMap` the map.

**Source jobs**: edit_sprite, adjust_sprite, sprite_new_direction and add_sprite_sound take `sourceJobId`: the `jobId` of a done item from `get_job` /
`wait_for_job` of an earlier sprite batch (yours; admins may use any). Ludo only works on sheets from the last 7 days, so older sources are rejected.
Edit the original sheet, not an earlier edit: every edit re-renders the whole animation and detail drifts.

**Quantity** (generate_sprite, transfer_sprite_motion, edit_sprite, sprite_new_direction): 1–4, default 2, each a separate paid Ludo job. When quantity is
omitted and 2 is blocked for your account, the highest allowed value is used; an explicit blocked quantity is an error. The other tools run one job.
Param Access can block operations, models and image types. There is no daily limit.

**Shared sheet fields**: `frames` 4, 9, 16, 25, 36, 49, 64 (64 needs duration ≥ 4); `loop`; `crop`; `gif`, `individualFrames`, `spritesheetWithBackground`
(extra outputs, default false); `usePixelGrid` (pack on the detected pixel grid, Ludo's default; false packs as ordinary art; ignored for forge-pixel);
`saveNormals` (normal map, 0.5 credits per sheet). On source operations every sheet field left out keeps the source's value.

### generate_sprite

Animate (or Keyframe Animation when `middleFrameUrl` is set): a start frame into a spritesheet and a video.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| startFrameUrl | string | — | Required. Image to animate |
| middleFrameUrl | string | — | Middle keyframe. Makes it a Keyframe Animation; motionPrompt becomes optional |
| endFrameUrl | string | — | Last frame; guides the final pose |
| motionPrompt | string | — | Up to 2000 chars. Required unless middleFrameUrl is set. One clear phrase for a known action |
| model | string | hydra | hydra, griffin-hd, ignis, ignis-pixel, hydra-plus (legacy), forge (legacy), forge-pixel (legacy) |
| duration | number | 3 | Seconds. hydra / hydra-plus: 3–5 in 0.5 steps. griffin-hd: 5 only (default 5). ignis / ignis-pixel / forge / forge-pixel: 1–5 in 0.5 steps |
| frames | number | 36 | See shared fields |
| frameSize | number | 0 | 32, 64, 96, 128, 192, 256, 384, 0 (max), -1 (AI 1.5x upscale), -9 (True Size) |
| marginMode | string | auto | auto, manual, none. manual needs marginHorizontal and/or marginVertical (0–1); auto and none reject them |
| imageType | string | sprite | sprite, sprite-vfx, item-icon, ui_asset, logo, sprite-tiling-horizontal, sprite-tiling-vertical, scatter-pattern, parallax_layer, tile, texture, portrait, card-art |
| loop / crop | boolean | true / false | |
| augmentPrompt | boolean | true | Let Ludo enhance the motion prompt |
| gif, individualFrames, spritesheetWithBackground, usePixelGrid, saveNormals | boolean | — | See shared fields |
| quantity | number | 2 | 1–4 |

Models: hydra (default) also returns an audio track; griffin-hd has the highest resolution and image fidelity, the pick for fine detail; ignis is for
low-end, cost-optimized animation (the cheapest; simple body shapes and simple actions) and also returns an audio track; ignis-pixel is for true pixel art (a perfect pixel grid at its
natural resolution). hydra-plus, forge and forge-pixel are legacy (see shared rules); they still run.
Credits per job: hydra 3 per second (minimum 9), hydra-plus 4 per second (minimum 12), griffin-hd flat 10, ignis, ignis-pixel, forge and forge-pixel 1.5 per second.
Item keys: `sprite-1`, `sprite-2`, ….

### transfer_sprite_motion

Motion Transfer: animate a sprite with the motion of a clip, a Ludo animation preset or one of the account's own animations.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| imageUrl | string | — | Required. The sprite (best made with image type sprite, sprite-vfx or ui_asset) |
| videoUrl | string | — | Motion clip (video file URL, e.g. a sprite result's `sprite.video.url`); up to 4 s works best |
| presetId | string | — | From list_sprite_presets |
| animationId | string | — | One of the account's own animations: list_my_animations, or create_animation (describe a move once, reuse it on any character) |
| perspective | string | — | Required with presetId / animationId: horizon (side), isometric, high (top-down), low, top (overhead) |
| direction | string | — | Required with presetId / animationId: N, NE, E, SE, S, SW, W, NW (where the camera stands) |
| mirror | boolean | false | With presetId or animationId (not videoUrl): left and right swapped |
| prompt | string | — | Extra instructions ("keep the cape still") |
| model | string | hydra | hydra, ignis, ignis-pixel, forge (legacy), forge-pixel (legacy). ignis takes no videoUrl |
| duration | number | shortest | Per model table. A longer clip or preset is compressed to fit: pass the preset's own duration |
| frameSize | number | 0 | 32–384 or 0 |
| marginMode | string | manual | manual (0.15 per axis unless set) or none. Keep margins tight; raise only the axis the motion needs |
| marginHorizontal / marginVertical | number | 0.15 | 0–1 |
| frames, loop, crop, output flags | | | See shared fields |
| quantity | number | 2 | 1–4 |

Send exactly one of videoUrl, presetId, animationId. Look through list_sprite_presets first; when no preset covers the move, check list_my_animations, then create_animation. Credits per job: hydra 3 per second, ignis and ignis-pixel 1.5 per second, forge and forge-pixel 2 per second. hydra and ignis also return an audio track.

### list_sprite_presets

| Field | Type | Notes |
|-------|------|-------|
| category | string? | Only this category (case-insensitive) |
| query | string? | Name or description contains this text |

Free. Returns `{ animations: [{ id, name, category, description, duration, previewUrl? }], categories, perspectives, directions }`.
`previewUrl` is Ludo's own URL and may change.

### edit_sprite

Edit Animation: re-render a sprite result as a new sheet; the source stays as it is.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| sourceJobId | string | — | Required. See Source jobs |
| editMode | string | prompt | prompt, outpaint, fix_loop, improve |
| prompt | string | — | Required for prompt (one short change, under ~50 words), optional for outpaint, not allowed for fix_loop / improve |
| referenceUrls | string[] | — | Up to 5 images (prompt / outpaint); two or three work best |
| model | string | source's | prompt mode only: hydra, griffin-hd, forge (legacy), forge-pixel (legacy) |
| duration | number | source's | prompt mode only, needs model |
| frameSize | number | source's | 32–384 or 0 |
| frames, loop, crop, output flags | | | See shared fields |
| quantity | number | 2 | 1–4 |

- **prompt**: re-prompt the animation. hydra 3 credits per second, griffin-hd flat 10, forge and forge-pixel 2 per second.
- **outpaint**: extend the frame beyond its borders. **fix_loop**: repair a bad loop (makes a new clip).
- **improve**: Ludo's "Improve Details": the Hydra+ detail pass on a Hydra animation, with the motion unchanged. Only for sheets made with hydra;
  other sources are rejected (a hydra-plus sheet already has the detail).
- outpaint, fix_loop and improve cost 1 credit per second of output (capped at the source length) and keep the source's sound.

### adjust_sprite

Adjust Sheet: re-export a sprite result with other sheet settings without generating it again. The motion is unchanged.

| Field | Type | Notes |
|-------|------|-------|
| sourceJobId | string | Required. See Source jobs |
| frameSize | number? | 32–384, 0 (max), -1 (AI 1.5x upscale, no gain under 512 px), -9 (True Size) |
| frames, loop, crop, output flags | | See shared fields; `usePixelGrid` false fixes a wrongly detected pixel grid |

Free (saveNormals costs 0.5). One job. Ludo allows 20 calls per 5 minutes.

### sprite_new_direction

New Direction: the same motion and timing of a sprite result, seen from another view (a front walk turned into the same walk from the back).

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| sourceJobId | string | — | Required. See Source jobs |
| startFrameUrl | string | — | Required. The character exactly as it must look in the new view; it becomes the first frame |
| endFrameUrl | string | — | Exact last frame (defaults to the first, for cycles) |
| prompt | string | — | Facing in the new view ("its back to the camera"), where the action goes |
| model | string | hydra | hydra, hydra-plus (legacy) |
| duration | number | source's | 3–5 in 0.5 steps |
| frameSize | number | source's | 32–384 or 0 |
| frames, loop, crop, output flags | | | See shared fields |
| quantity | number | 2 | 1–4 |

Make the new-view image with rotate_sprite from the first frame of the source (`individualFrames`, or the start frame you animated).
Credits per job: hydra 3 per second (minimum 9), hydra-plus 4 per second (minimum 12), for the source's length.

### add_sprite_sound

| Field | Type | Notes |
|-------|------|-------|
| sourceJobId | string | Required. See Source jobs |
| prompt | string? | The sound you want |

Sound: makes a sound effect for a sprite result. hydra, hydra-plus and ignis sheets already have one; use it for other sheets or to replace it.
3 credits, one job. The done item holds the source sheet and video plus the new `sprite.audio`.

### generate_sprite_pose

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| imageUrl | string | — | Required. The sprite (sprite-type images only, not icons or screenshots) |
| pose | string | — | Required. Idle (Front / Back / Left Facing / Right Facing), Walk and Run (Left / Right / Towards the camera / Away from the camera), Crouching, Crawling, Sitting, Defending / Blocking, Attack Ready, Jump Preparation, Sleeping, Flying, Other |
| description | string | — | Extra instructions; required for Other |
| variations | number | 1 | 1–4 images in the one job |
| saveNormals | boolean | false | Normal map per image (free) |

Generate Pose: re-poses the character. 0.5 credits per image. Feed an image and its `motionPrompt` into generate_sprite for the best animation.

### rotate_sprite

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| imageUrl | string | — | Required. The sprite (sprite-type images only) |
| cameraRotation | number | — | Required. 0 front, 45 front-right, 90 right, 135 back-right, 180 back, -135 back-left, -90 left, -45 front-left |
| cameraElevation | number | — | 0 eye level, 30 elevated, 60 high angle; omit to keep |
| variations | number | 1 | 1–4 images in the one job |
| saveNormals | boolean | false | Normal map per image (free) |

Rotate Sprite: the same character and pose from another camera angle. 0.5 credits per image.

### create_normal_map

| Field | Type | Notes |
|-------|------|-------|
| imageUrl | string | Required. Any image (sprite, prop, tile, background), at most 15 MB |

Normal Map: one normal map of the image for 2.5D lighting: lossless WebP, same size and alpha. OpenGL convention: Unity and Godot read it as is,
Unreal needs green flipped. Pixel art gets one normal per art pixel; seamless tiles stay seamless. Free, one job. Ludo allows 20 calls per 5 minutes,
120 per hour and 1000 per day; beyond that the job fails with HTTP 429. The done item's `images[0].url` is the source image, `images[0].normalMap` the map.
Sprite generations, generate_sprite_pose and rotate_sprite can return their maps directly with `saveNormals`.

### Directional Sprites (Ludo.ai): shared rules

Ludo Directional Sprites: a character as a rigged 3D model, rendered in a style from up to 8 directions, with optional weapons and accessories. Every step of the web app is one tool.

**Step order**
1. (optional) `create_character_image`: a character image ready for 3D, from a prompt and/or your own image.
2. `create_3d_model` with `rig: true` (or `rig_3d_model` on a GLB you have): a rigged GLB.
   Weapons & accessories (optional): `create_3d_model` without rig for each item GLB, `fit_accessory` (repeat with size / move / turn until the checks and previews look right), then `attach_accessories`: one GLB holding every item, used as `modelUrl` from here on.
3. (optional) animation, at most one: `presetId` (list_sprite_presets), `animationId` (create_animation or list_my_animations), `animationUrl` (a clip GLB from animate_3d_model) or `modelAnimation` (a clip inside the GLB). None = a still.
4. `list_directional_styles`: `styleId` + `styleParams`.
5. `preview_directional_sprites` (free) to check the style and camera.
6. `create_directional_sprites` (one sheet per direction) or `create_directional_stills` (one image per direction).

All file inputs are img-gen file URLs: images must be image files, `modelUrl` / `itemUrl` / `animationUrl` GLB files (`.glb`, up to 30 MB; upload your own with create_upload_url).
**GLB results are permanent img-gen file URLs** (never Ludo URLs): pass `model3d.model.url`, `clips[].glb.url` and the accessory `model.url` back as `modelUrl` / `itemUrl` / `animationUrl`.
Queued tools return `{ batchId, app: "directional-sprites", operation, model, items: [{ key, label, jobId }], quantity, hint }`; poll with `wait_for_job` (`timeoutSec` 120, repeat while `pending`). Jobs can take hours: like Sprite Gen, they stay `pending` while the server still polls Ludo (48 h) and copies the files (72 h).
Sync tools (list_directional_styles, list_my_animations, preview_directional_sprites, fit_accessory, attach_accessories) are free and answer at once. Preview frame URLs are Ludo's own and temporary.
Quantity (create_character_image, create_3d_model only): 1–4, default 1, each a separate paid Ludo job; the other tools run one job. Param Access can block each operation and sync action (api `ludo`).
`list_capabilities.directional` lists the operations, tools, credits, generators, rig types, joint namings, accessory kinds and points, perspectives, directions, limits and the step order.

**Done items by operation**
- create_character_image, create_directional_stills: `images: [{ url, mimeType, direction?, width?, height?, normalMap? }]`
- create_3d_model, rig_3d_model: `model3d: { model: { url, mimeType }, snapshots, rigged, generator? }`; `images` holds the snapshots
- create_animation: `animations: [{ id, name, prompt?, caption?, duration?, loop?, preview? }]`; `videos` holds the mp4 previews
- animate_3d_model: `clips: [{ glb, preview?, clipName?, presetId?, animationId?, mirror? }]`; `videos` holds the mp4 previews
- create_directional_sprites: `sheets: [{ direction, spritesheet, video?, gif?, frames?, normalMap?, numFrames, numCols, numRows, fps, render3d }]`; `images` holds the sheets, `videos` the sheet videos

**Camera fields** (preview / sprites / stills): `perspective` horizon, low, isometric (default), high, top or degrees 0–90; `directions` S, SW, W, NW, N, NE, E, SE or degrees (S = 0, SW = 45, W = 90, N = 180), 1–8, each once (default `["SW"]`).
**Style fields**: `styleId` (default render_3d), `styleVersion`, `styleParams` (setting name → one of its option values).

### create_character_image

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| prompt | string | — | The character, or with imageUrl the changes to make |
| imageUrl | string | — | Your own image, redrawn as a 3D render |
| quantity | number | 2 | 1–4 jobs |

0.5 credits per image. Pass prompt, imageUrl or both.

### create_3d_model

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| imageUrl | string | — | Required. Front view |
| backImageUrl | string | — | prop only: the same subject from behind |
| generator | string | prop | prop (4 credits) or hero (15 credits, front image only) |
| rig | boolean | false | Also rig (general rig): +1 credit when it comes back rigged. Needed to animate and to render directional sprites |
| jointNaming | string | smpl | With rig: smpl, mixamo, humanik, unreal, godot, rigify, vroid |
| targetNumFaces | number | 50000 | 1000–200000 |
| textureSize | number | 2048 | 1024 or 2048 |
| textureType | string | pbr | pbr, simple, none |
| quantity | number | 2 | 1–4 jobs |

For a weapon or accessory item, leave `rig` off.

### rig_3d_model

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| modelUrl | string | — | Required. GLB |
| rigType | string | general | general, humanoid, game, humanoid_template, humanoid_template_hands |
| jointNaming | string | smpl | As create_3d_model |

1 credit.

### create_animation

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| prompt | string | — | Required. The move, e.g. "overhead sword slash" |
| numVariants | number | 1 | 1–4, each charged |
| seed, duration, loop, startIdle, endIdle | — | — | Omit to infer from the prompt |
| leftHand, rightHand | string | — | relaxed, fist, grip, open, flat, point, thumbs_up, pinch, v_sign, claw |
| keepFacing | boolean | — | No turning |
| augmentPrompt | boolean | true | Ludo rewrites the prompt |

0.5 credits per animation. Saved on the Ludo account: pass `animations[].id` as `animationId`. Check list_sprite_presets first.

### animate_3d_model

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| modelUrl | string | — | Required. Rigged GLB |
| prompt | string | — | Motion from text (animate3DModel) |
| mode, numVariants, loop, augmentPrompt | — | rot_trans, 4, true, true | prompt only |
| presetId / animationId | string | — | Retarget a preset or a Ludo animation (animate3DModelPreset) |
| mirror, inPlace | boolean | — | presetId / animationId only |

Exactly one of prompt, presetId, animationId. 0.5 credits per call. Pass `clips[].glb.url` as `animationUrl`.

### list_directional_styles / list_my_animations

Free. Styles: `{ styles: [{ id, label, description, family, pixelArt, thumbnailUrl, version, settings, advancedSettings }], perspectives, directions }`.
My animations (`limit`, `before`): `{ animations: [{ id, name, prompt, duration, loop, preview }] }`.

### preview_directional_sprites

Same model, animation, camera, style and travel fields as create_directional_sprites, plus `targetFrameSize` (default 256). Free and synchronous.
Returns `{ previews: [{ direction, frameUrls, width, height, numFrames, fps, render3d }] }`. One preview at a time: a newer one cancels the older.

### fit_accessory

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| modelUrl | string | — | Required. Rigged GLB |
| itemUrl | string | — | Required. Item GLB (create_3d_model without rig) |
| name | string | — | "rusty sword"; guesses kind when kind is omitted |
| kind | string | — | blade, gun, staff, bow, shield, helmet, hat, back, other |
| point / bone | string | — | right_hand, left_hand, head, back, hips, or a bone name |
| size | number | 100 | % of the automatic fit |
| move / turn | object | — | `{ forward, up, out }`: % of height / degrees |
| flip, grip | boolean | false, true | Hold by the other end; close the hand |
| preview, previewPresetId | — | true, walk_forward | Previews of the model walking with the item |

Free. Returns `{ kind, point, bone, placement, checks, points, model?, previewUrls }`. Each call starts from the automatic fit: send the full adjustment every time.

### attach_accessories

| Field | Type | Notes |
|-------|------|-------|
| modelUrl | string | Required. Rigged GLB |
| items | object[] | 1–8 of `{ itemUrl, name?, placement?, kind?, point?, bone?, size?, move?, turn?, flip?, grip? }`; at most one item closing each hand |

Free. Returns `{ model: { url, mimeType }, items, kept }`: the GLB of the model holding every item. Items stay rigid through every direction and animation.

Example (a knight with a sword and a shield):

```text
create_3d_model { imageUrl: <knight>, rig: true }                 → model3d.model.url = KNIGHT
create_3d_model { imageUrl: <sword> }                             → SWORD
create_3d_model { imageUrl: <shield> }                            → SHIELD
fit_accessory { modelUrl: KNIGHT, itemUrl: SWORD, kind: "blade" } → placement P1 (repeat with size / move / turn if checks are off)
fit_accessory { modelUrl: KNIGHT, itemUrl: SHIELD, kind: "shield" } → placement P2
attach_accessories { modelUrl: KNIGHT, items: [{ itemUrl: SWORD, placement: P1 }, { itemUrl: SHIELD, placement: P2 }] } → model.url = ARMED
create_directional_sprites { modelUrl: ARMED, presetId: "walk_forward", directions: ["S", "SW", "W", "NW", "N"] }
```

### create_directional_sprites

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| modelUrl | string | — | Required. GLB (rigged to animate) |
| animationUrl / presetId / animationId / modelAnimation | string | — | At most one; none = one frame per sheet |
| mirror | boolean | — | presetId / animationId only |
| perspective, directions | — | isometric, ["SW"] | See camera fields |
| styleId, styleVersion, styleParams | — | render_3d | See style fields |
| keepHorizontalTravel, keepVerticalTravel | boolean | false, true | Ground travel / jumps |
| targetFrameSize | number | 640 | 32–2048, the longer side |
| numFrames | number | 36 | Per sheet |
| loop | boolean | — | Omit to follow the animation |
| gif, individualFrames | boolean | false | Extra outputs |
| saveNormals | boolean | false | Normal map per sheet, 0.5 credits each |

1 credit per direction.

### create_directional_stills

`modelUrl` (required), camera fields, style fields, `saveNormals`. The model as it is, in its rest pose. 0.5 credits per direction (+0.5 per normal map).

### Tripo 3D: shared rules

Tripo API v3: 3D models (GLB) from text, one image or several views, Gaussian splats, helper images, texturing, format conversion (FBX, USDZ, OBJ, STL, 3MF, GLTF), auto rig, preset animations and mesh tools. Four tools cover every operation; `list_tripo_options` (free) lists the operations, models, option fields, animation presets and credits.

**Tools**
- `tripo_generate_model` `{ mode: "text" | "image" | "multiview", prompt, negativePrompt, imageUrl, imageUrls, model, options, quantity }`: text needs `prompt`, image `imageUrl`, multiview `imageUrls` = [front, left, back, right] (front required, empty string for a missing view).
- `tripo_image_to_splat` `{ imageUrl, options, quantity }`: a `.splat` file.
- `tripo_generate_image` `{ operation: "text-to-image" | "image-to-image" | "image-to-multiview" | "edit-multiview", prompt, imageUrl, imageUrls, sourceJobId, model, options, quantity }`: helper images. image-to-multiview takes `imageUrl` and returns front / left / back / right views. edit-multiview takes `prompt` and the `sourceJobId` of an image-to-multiview job.
- `tripo_process_model` `{ operation: "texture" | "convert" | "rig-check" | "rig" | "retarget" | "segment" | "complete" | "decimate" | "smart-segment" | "uv", sourceJobId, modelUrl, imageUrl, prompt, options }`: one run per call.
- `list_tripo_options`: free.

**Models**: 3D `model` default `v3.1-20260211` (H3.1); also `v3.0-20250812`, `v2.5-20250123` (legacy), and the P series `P2-20260801` (clean topology, quads) and `P1-20260311` (low poly). Image `model` for text-to-image / image-to-image: seedream_v5, seedream_v4, banana, banana2, banana_pro, chat_image_2, chat_image_2.5_flare, chat_image_2.5_sunburst. `options` are Tripo body fields in snake_case (for example `texture`, `texture_quality`, `geometry_quality`, `quad`, `face_limit`; convert: `format` required; rig: `rig_type`, `spec`; retarget: `animations`, 1–5 of the 117 `preset:…` ids from list_tripo_options); unknown fields are rejected.

**Follow-ups**: pass the `jobId` of an earlier Tripo item as `sourceJobId` (same account; it is sent to Tripo as that job's task id, so nothing is re-uploaded). retarget needs a rig job, complete a segment job, edit-multiview an image-to-multiview job. Without a source job pass `modelUrl`: an img-gen file URL of a GLB, GLTF, FBX, OBJ or STL (≤ 100 MB; upload with create_upload_url).

**Typical chains**
```
text → model → rig → retarget
tripo_generate_model { mode: "text", prompt: "low poly knight" }            → JOB1 (GLB)
tripo_process_model { operation: "rig-check", sourceJobId: JOB1 }           → riggable, rig_type (free)
tripo_process_model { operation: "rig", sourceJobId: JOB1 }                 → JOB2 (rigged GLB)
tripo_process_model { operation: "retarget", sourceJobId: JOB2, options: { animations: ["preset:idle", "preset:walk"] } } → JOB3 (animated GLB)

image → multiview → model
tripo_generate_image { operation: "image-to-multiview", imageUrl: <character> } → JOB1 (4 views)
tripo_generate_model { mode: "multiview", imageUrls: [front, left, back, right] } → JOB2 (urls from JOB1 files)

model → FBX
tripo_process_model { operation: "convert", sourceJobId: JOB1, options: { format: "FBX" } } → JOB (FBX file)
```

**Submit answer**: `{ batchId, app: "tripo-3d", operation, model, items: [{ key, label, jobId, taskId }], quantity, credits, hint }` (`credits` = estimate for all items).

**Results** (`wait_for_job` / `get_job`): a done item has `operation`, `model`, `taskId`, `files: [{ key, role, url, mimeType, format }]`, `info` and `credits`; `images` holds the image files (preview first, for get_image). `role` is model, preview, image, view or other; the model file is the `role: model` entry. `info` holds `riggable`, `rig_type`, `normalize_scale`, part names. All URLs are permanent img-gen file URLs (never Tripo URLs). History shows the preview image with the model files attached (image-to-multiview: one item per view; rig-check: none).

**Credits**: 1 credit = $0.01. Examples (images: seedream and banana 5, banana2 / chat_image_2 10–15, banana_pro 15–20): text → textured model 20, image → textured model 30, detailed texture +10, detailed geometry +20, rig 25, retarget 10 per animation, convert 5, segment 40, Smart UV 20, rig-check free, splat 30, image-to-multiview 10. The submit answer shows the estimate; the result shows Tripo's `credits`. No credits left = a 402 error. Tripo freezes credits at submit and releases them when a task fails.

**Polling**: Tripo jobs take 1–5 minutes. Call `wait_for_job` (`timeoutSec` ≤ 60) repeatedly while items are `pending`. Items stay `pending` while the server can still finish them: it polls a Tripo task for 45 min and retries the file copies for 6 h, so a slow item can take hours. Keep polling; never resubmit a pending item. Concurrency per Tripo category (H models 10, P models 5, images 1, animation 10, processing 5, mesh 10): a full category answers 429; wait and resubmit.

**Quantity**: 1–4, default 1, one paid task each, only for the generation tools; processing tools run one task. Param Access (api `tripo`) can block `operation`, `model`, `quantity`, `texture_quality` (fast, standard, detailed, extreme), `geometry_quality` (standard, detailed).

### search_vfx

| Field | Type | Notes |
|-------|------|-------|
| query | string? | Words, all must match file name, element, function, shape, kind, color or tags. `is:hidden` / `is:public` filter by visibility. Empty = newest first |
| limit | number? | 1–100, default 20 |
| offset | number? | Paging offset |

Searches the team VFX texture library (flipbooks and single textures; tags come from the PNG metadata and manual edits).
Returns `{ total, offset, hasMore, items }`; each item has `id, url, thumbUrl, fileName, width, height, element, function, shape, kind, color, tags, grid`.
`url` is a permanent PNG URL, usable directly as a reference URL in generate_* tools. `grid` is `{ cols, rows }` for flipbooks (every cell is a frame).
Synchronous, no generation cost.

### get_vfx

| Field | Type | Notes |
|-------|------|-------|
| ids | string[] | 1–50 ids from search_vfx |

Returns `{ items, missing? }` in the same shape as search_vfx.

### update_vfx

| Field | Type | Notes |
|-------|------|-------|
| items | object[] | 1–50 of `{ id, grid?: { cols, rows }, addTags?, removeTags?, audience? }` |

Bulk-edits VFX Library items (same rights as editing in the app). `grid` sets the flipbook frame grid; every cell plays.
`audience`: `"debugger"` hides an item, `null` makes it visible to everyone; only admins with the debugger role and VFX managers
may set it (others get `forbidden`).
Returns `{ updated, errors? }`.

### create_vfx_upload_session

Returns `{ token, expiresInSeconds, origin, usage }`: a 1-hour token for
`VFX_UPLOAD_TOKEN=<token> python scripts/vfx-library/upload_folder.py <folder>` (img-gen repo). Tags and the flipbook grid are
read from each PNG's `ezg.tags` metadata, so tag the files before uploading. `ezg.tags.audience` = `"debugger"` hides an item:
only admins with the debugger role and VFX managers can find or open it (search_vfx, get_vfx and the app skip it for everyone else).

### get_job

| Field | Type | Notes |
|-------|------|-------|
| id | string | The batchId from a generate_* tool |

Returns batch status with image URLs (`videos[].url` for generate_video, `sprite` for Sprite Gen sheets, `images` for pose / rotate) when done. Items still pending past the server time limit are
returned as `error` with code `timeout` — resubmit them with `retry_job_items`. Sprite and 3D items (Ludo, Tripo) stay `pending` while the server finalizer can still finish them (hours; up to 5 days for Ludo) and the batch then carries a `hint`.

### wait_for_job

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| id | string | — | Required |
| timeoutSec | number | 90 | 5–120 |

Polls until completion or timeout. Returns same shape as get_job. Video and sprite batches usually need several calls; sprite and 3D batches can need hours of calls. A `pending` result is not a failure.

### send_to_discord

Posts images or videos to the team Discord (MCP thread), like the web app's Send to Discord button.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| batchId | string | one of batchId/imageUrl | Batch from a generate_* tool (must be yours) |
| items | string[] | no | Item keys or job ids from that batch; default all |
| imageUrl | string | one of batchId/imageUrl | img-gen file URL |
| prompt | string | no | Embed prompt; defaults to the stored prompt for a batch |

Returns `{ sent, sentAsLink, skipped, failed, results }`. Generations by non-admin users are already
posted automatically when each image finishes (`isAdmin` is in whoami), so calling this posts again.
Images and videos over 10 MB are posted as a file link (videos attach as `video.mp4`). Pending or failed items are returned as `skipped`.

### retry_job_items

Resubmits failed or timed-out items with their original request. Same batchId; retried items get new job ids.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| batchId | string | yes | Your batch |
| items | string[] | no | Item keys or job ids; default all failed items |

Returns `{ batchId, retried: [{ key, oldJobId, newJobId }] }`. Then call `wait_for_job`.

### list_my_jobs

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| limit | number | 10 | 1–30, newest first |
| app | string | — | Filter by app id |
| includeStatus | boolean | true | false = ids only (faster) |

Returns `{ batches: [{ batchId, app, createdAt, status, items, done, pending, failed }] }` for the last 48 h.

### open_link

Reads what an img-gen web deep link points at, without opening the website. When a person pastes an img-gen link, call this instead of fetching the page.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| url | string | yes | The full link as shared |

| Link | Returns |
|------|---------|
| any page with `?history=<id>` | `history.item`: the History item, its files and inputs |
| `/archive?event=<id>&pos=<n>` | `result.event` (+ `output` at `pos`): the archive event, prompt, every ref and output |
| `/archive?q=…&feature=…&model=…&source=…&status=…` | the first 20 matching events |
| `/vfx-library?id=<id>` / `?q=…` | one VFX item / the first 20 search hits |
| `/sprite-gen?job=<id>`, `/video-gen?job=<id>`, `/tripo-3d?job=<id>` | the job and its result, or `pending` |
| `/api-logs?log=<id>` | one API call log (admins) |
| `/param-access?api=…&user=…` | that user's rules plus the all-users rules (admins) |
| `/technical-art-tools?tool=…` | the tab name only |

Visibility is the web's: members see only their own items, admins see everyone's; anything else is refused with a reason. File URLs are permanent; pass an image URL to `get_image` to see it.

### get_image

Returns an image as an MCP image block (for clients that cannot open URLs). Image-only: video items and URLs are rejected, so use `videos[].url` instead.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| url | string | one of url/batchId | img-gen file URL |
| batchId | string | one of url/batchId | Your batch |
| item | string | no | Item key or job id; default first item |
| index | number | no | Image index inside the item; default 0 |

Images over 3.2 MB are not inlined (never downscaled); the result holds the `url` instead.

### create_upload_url

Hash-verified PUT for a local image, video or audio file up to 100 MB, byte-intact. Use it when base64 would pass ~3 MB.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| sha256 | string | yes | 64 hex chars of the file; run `shasum -a 256 FILE` (macOS) or `sha256sum FILE` |
| mimeType | string | yes | image/png, image/jpeg, image/webp, video/mp4, video/quicktime, video/webm, audio/mpeg, audio/wav |
| name | string | no | Filename hint |

Returns `{ method, uploadUrl, headers, curl, expectedUrl, expiresInSeconds }` (short-lived). Run the `curl`
with the file path (or PUT the raw bytes with `headers`), then use `expectedUrl` as the reference URL. A 400 `sha256 mismatch` means re-hash the file; do not retry the same request.

## Workflow

1. Call \`list_capabilities\` to see available apps, models, blocked params, and limits.
2. Upload reference images with \`upload_reference_image\` (or \`create_upload_url\` for files over
   ~3 MB). All image URLs passed to generation tools must be file URLs; result URLs from earlier
   batches work too (e.g. chain into \`upscale_image\`).
3. Call a generation tool (any \`generate_*\` tool above, or \`upscale_image\`). Multi-step apps
   have sync helpers first: \`split_story_into_scenes\` / \`analyze_style_reference\` (storyboard),
   \`generate_npc_main_texts\` (NPC), \`analyze_room\` (iso room), \`describe_image\` (any).
4. Poll with \`wait_for_job\` (preferred) or \`get_job\`. Resubmit failed items with
   \`retry_job_items\`; find a lost batchId with \`list_my_jobs\`.
5. Result image URLs are permanent file URLs; download or reuse them any time.
6. Video: `generate_video` returns a batchId like any tool, but clips take minutes (Omni and Veo up to ~20 min, Seedance up to ~50 min).
   Keep calling `wait_for_job`; read `videos[].url` (permanent). `get_image` is image-only.
   For a person with a real-looking face in Seedance, follow Real faces in Seedance below.
7. Sprite: each Sprite Gen job runs about 2–3 min. Keep calling `wait_for_job` (`timeoutSec` 120); read `sprite.spritesheet.url` and `sprite.video.url`
   (pose / rotate: `images[].url`). For edit_sprite, adjust_sprite, sprite_new_direction and add_sprite_sound pass the item's `jobId` as
   `sourceJobId` within 7 days. New view of an animation: rotate_sprite on its first frame → sprite_new_direction.
8. Tripo 3D: each task takes 1–5 min. Keep calling `wait_for_job` (`timeoutSec` ≤ 60, repeat while `pending`); read `files[]` (`role: model` = the GLB / FBX, `preview` = the render). Follow-ups pass the item's `jobId` as `sourceJobId`.

## Real faces in Seedance

Seedance (2.0 series and 2.5) refuses frames, reference images and reference videos that show a real person's face.
The job fails with `InputImageSensitiveContentDetected.PrivacyInformation`.
Official rule: BytePlus ModelArk, "Use trusted model outputs as input assets" in
https://docs.byteplus.com/en/docs/ModelArk/seedance-portrait-asset-guide

BytePlus skips the face check only for these outputs of the img-gen BytePlus account, for 30 days after they were made:

- images from seedream-5.0-pro or seedream-5.0-flash made from text only (no reference image of any kind);
- videos made by Seedance, and their last frames (`videos[].lastFrameUrl`);
- `asset://<id>` images from the portrait library (add_portrait_asset), once `Active`.

These are checked, and a real face in them is blocked:

- Seedream images made with any reference image (generate_images with referenceImageUrls or structureImageUrls,
  generate_studio_image with material, layout or style images, and every other tool that sends an image);
- seedream-4.5 images, even from text only;
- Gemini and GPT images, Omni and Veo videos, and the Seedance poster (`posterUrl`);
- uploaded files (upload_reference_image, create_upload_url);
- trusted images that are more than 30 days old.

An image without a face passes either way. BytePlus knows its own outputs by their bytes. img-gen stores them byte-intact,
so the img-gen file URL of a trusted image works. There is no need for the BytePlus URL, which expires after 24 h.

### Make a character clip

1. Make the face images with generate_images, model seedream-5.0-pro or seedream-5.0-flash, and no referenceImageUrls or
   structureImageUrls. No reference image is allowed, so describe the whole character in the prompt: face, hair, clothes, style.
2. In get_job / wait_for_job, each image of a done Seedream item carries `seedanceTrusted`, `seedanceTrustedUntil` and
   `seedanceNote`. `seedanceTrusted` is `true` for seedream-5.0 images made from text only. `seedanceTrustedUntil` is the
   time (ISO) after which BytePlus checks the image again. `seedanceNote` says why in one sentence.
3. First clip: pass one image as startFrameUrl. To keep the same character across shots, pass several images of it as
   referenceImageUrls (@image1, @image2 … in the prompt). Set returnLastFrame: true.
4. Next clip: pass the previous clip's `videos[].lastFrameUrl` as startFrameUrl. It is trusted too. Repeat for each clip.
5. Use the images within 30 days. After that, make new ones.

### Warnings before submit

Before it submits a Seedance job, generate_video looks up where each frame, reference image and reference video came from.
Each input that BytePlus will check adds a line to the result's `notes`: the field (for example `referenceImageUrls[0]`),
its URL, that a real face in it will be blocked, and why. It is only a warning. The job is still submitted, because an
input without a face passes.

### When a job is blocked

The item's error names each blocked input, its URL, where it came from (for example Seedream with reference images,
GPT, Gemini or an upload), and what to do next. BytePlus counts `content[0]` as the prompt, so `content[1]` is the
first image you sent.

### Portrait library (`asset://`)

Use it for a face image that BytePlus does not trust, for example a character made by Seedream from a reference image.

1. Call add_portrait_asset with the image URL. BytePlus reviews it; `status` is `Processing`.
2. Call get_portrait_asset with the `assetId` until `status` is `Active`. `Failed` means BytePlus refused it; read `error`.
3. Pass `assetUrl` (`asset://<id>`) to generate_video with a Seedance model, as startFrameUrl, endFrameUrl or a
   referenceImageUrls item.

The portrait library works only after the owner adds BytePlus IAM access keys to the img-gen server. Until then both tools
return a "not configured" error. Use the Seedream 5.0 text-only flow above instead.

## Rights & param rules

Access follows web UI rules. Blocked model/param combinations return an error.
Call \`list_capabilities\` to inspect \`blockedParams\` before generating.

## Limits

- **16 image jobs** max per tool call (Gemini). Split larger batches across calls.
- **quantity**: ≤ 8 for generate_images, ≤ 4 for storyboard/ability variants.
- **base64 upload**: ≤ 4 MB decoded. Prefer \`sourceUrl\` or existing file URLs.
- **Video**: quantity ≤ 4 per call; clips count against the shared per-user daily video limit
  (same pool as the web Video Gen app; `list_capabilities.video` shows `dailyLimit` / `usedToday`).
  Pending video items time out after 20 min (Omni, Veo) or 50 min (Seedance); images after 360 s.
- **Sprite**: quantity ≤ 4 per call (each a separate paid Ludo job, no daily limit); pose / rotate: ≤ 4 images in one job. Source jobs must be
  ≤ 7 days old. Pending sprite items time out after 30 min.
- **Tripo 3D**: pending items time out after 60 min; quantity ≤ 4 per call for generation tools (each a separate paid task, 1 credit = $0.01); processing tools run one task. Model files ≤ 100 MB. Tripo concurrency is per category (429: wait and resubmit).
- **History**: MCP results appear in the web History. Sprite sheets show on the Sprite Gen tab as kind sprite, pose / rotate images as kind image, Tripo previews and images as kind image (app tripo-3d, model files attached), source mcp.
- **Result URLs**: permanent; `list_capabilities` reports `urlTtlHours: null`.

## Models

**Image models**: gemini-3.1-flash-lite-image (default), gemini-3.1-flash-image,
gemini-nano-banana-2.1, gemini-3-pro-image, gpt-image-2.5-sunburst,
seedream-5.0-pro, seedream-5.0-flash, seedream-4.5.

**Text models**: gemini-3.5-flash (default), gemini-3.1-flash-lite, gemini-3-flash-preview.

- Extended aspect ratios (1:4, 1:8, 4:1, 8:1) only on gemini-3.1-flash-image and gemini-nano-banana-2.1.
- `thinkingLevel` (Effort) is accepted by every Gemini image tool: gemini-3.1-flash-lite-image and
  gemini-3.1-flash-image take minimal or high; gemini-nano-banana-2.1 takes minimal, medium or high.
  Not supported on gemini-3-pro-image or GPT. Omit for the default; see list_capabilities `thinkingLevels`.
- gemini-3.1-flash-lite-image is clamped to 1K resolution.
- GPT model supports ratios between 1:3 and 3:1 only.
- Seedream (BytePlus): seedream-5.0-pro and seedream-5.0-flash output 1K or 2K, ≤ 10 reference images;
  seedream-4.5 outputs 2K or 4K, ≤ 14 reference images. Another resolution snaps to the nearest one the
  model supports. No `thinkingLevel`, no `gpt` params. Outputs carry no watermark.
- Only seedream-5.0-pro and seedream-5.0-flash images made from text only can be Seedance frames with a real face
  (`images[].seedanceTrusted` in get_job / wait_for_job). See Real faces in Seedance.

**Video models**: byteplus-seedance-2.0 (default), byteplus-seedance-2.0-fast, byteplus-seedance-2.0-mini, byteplus-seedance-2.5,
gemini-omni-1.1-flash, veo-3.1-generate-preview, veo-3.1-fast-generate-preview, veo-3.1-lite-generate-preview.

- Gemini Omni Flash (`gemini-omni-1.1-flash`): 16:9 or 9:16, 360p/720p (default)/1080p/4k (1080p and 4k
  are upscaled), 3–10 s at 24 fps chosen by the model (no durationSeconds), always with audio. Up to 3 images:
  startFrameUrl, startFrameUrl + endFrameUrl, or referenceImageUrls as subject references.
  No negativePrompt, seed, generateAudio or referenceVideoUrls. About $0.10 per second at 720p.
- Veo: 16:9 or 9:16, 4/6/8 s, 720p/1080p (1080p needs 8 s), start frame only, always with audio.
  Google currently returns 404 "no longer available" for all three Veo 3.1 preview ids.
- Seedance: aspect ratios adaptive, 16:9, 9:16, 1:1, 4:3, 3:4, 21:9; start+end frame; image, video and audio references;
  durationSeconds -1 lets the model pick the length; returnLastFrame on all four.
  - 2.0: 480p–4k, 4–15 s, ≤ 9 images + 3 videos + 3 audios. About $0.07 (480p) / $0.15 (720p) / $0.37 (1080p) / $0.78 (4k) per second.
  - 2.0-fast: 480p/720p, 4–15 s, same reference limits. About $0.06 / $0.12 per second.
  - 2.0-mini: 480p/720p, 4–15 s, same reference limits. About $0.04 / $0.08 per second.
  - 2.5: 480p/720p/1080p, 4–30 s, ≤ 30 images + 10 videos + 10 audios (audio alone allowed),
    draft → draftTaskId 1080p final, mp4 or mov, taskType. About $0.103 (480p) / $0.231 (720p) / $0.569 (1080p) per second.
  Prices are without video input; video input costs more.

## Example prompts

### Quick batch generation

> Generate 4 images of a medieval knight in different poses. Use 16:9 aspect ratio
> and gemini-3.1-flash-image model at 2K resolution.

### Storyboard from description

> Split this story into scenes: "A cat discovers a hidden garden behind a library,
> befriends a fox, and together they find a treasure map." Then generate a storyboard
> with a watercolor style at 16:9.

### Ability concept art

> Upload the character image, then generate concept art for these skills:
> 1. Fireball (intensity 8) — A massive sphere of flame launched forward
> 2. Ice Shield (intensity 5) — A crystalline barrier of frost
> 3. Thunder Strike (intensity 10) — Lightning called from the heavens

### Seedance clip of a person

> Make 4 portraits of a 30-year-old chef in a white jacket, short black hair, warm kitchen light, with seedream-5.0-flash
> at 9:16 2K and no reference images. Make a 5 s byteplus-seedance-2.5 clip with the best one as the start frame and
> returnLastFrame on, then a second clip that starts from its last frame.
