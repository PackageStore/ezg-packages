---
description: Generate a 2D UI game icon in a cute sticker art style
---

# Generate Icon Workflow

When the user runs `/gen-icon [icon_description]`:

1. **Understand Request**: Extract the `icon_description` from the user's slash command.
2. **Execute Generation**: Call the `generate_image` tool using EXACTLY the following prompt, replacing `[icon_description]` with the user's input:

   `A 2D game UI icon for [icon_description]. Designed in a cute sticker art style. The art style must be identical to sticker art: thick dark brown outlines on the subject, a thick white die-cut sticker border around the whole shape, soft pastel and bright colors, smooth cell-shaded highlights and shadows. Add small floating decorative geometric dots and sparks around it. Set on a flat light gray background.`

   *ImageName*: Format the `icon_description` into snake_case. Replace spaces with underscores.

3. **Output**: Once the image generation is complete, respond to the user and present the generated image. Do not perform any other unrelated actions.
