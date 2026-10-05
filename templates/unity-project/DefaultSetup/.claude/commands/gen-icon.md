---
description: Generate a 2D UI game icon in the project's art style (ArtStyle.md), falling back to a cute sticker style
---

# Generate Icon Workflow

When the user runs `/gen-icon [icon_description]`:

1. **Understand Request**: Extract the `icon_description` from the user's slash command.
2. **Load the project's icon style**: Read `.claude/docs/ArtStyle.md` § Icon & hero art (and `Read` the board images it names as references). When it is filled in (`Status` is not `template`), build the prompt as `<its base AI prompt>` + `A 2D game UI icon for [icon_description].`, pass the reference images it lists to the tool when the tool accepts references, and follow its technical rules (one object per image, background, size). That style replaces the default below — icons must match the rest of the game.
3. **Execute Generation**: Call the `generate_image` tool with the prompt from step 2. Only when ArtStyle.md is missing or still `Status: template`, use EXACTLY the following default prompt, replacing `[icon_description]` with the user's input:

   `A 2D game UI icon for [icon_description]. Designed in a cute sticker art style. The art style must be identical to sticker art: thick dark brown outlines on the subject, a thick white die-cut sticker border around the whole shape, soft pastel and bright colors, smooth cell-shaded highlights and shadows. Add small floating decorative geometric dots and sparks around it. Set on a flat light gray background.`

   *ImageName*: Format the `icon_description` into snake_case. Replace spaces with underscores.

4. **Output**: Once the image generation is complete, respond to the user and present the generated image. Say which style was used (ArtStyle.md or the default sticker style). Do not perform any other unrelated actions.
