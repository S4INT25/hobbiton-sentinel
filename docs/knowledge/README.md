# Knowledge bases and report prompts

Source of truth for the markdown that drives the analytics agent. Previously kept outside the
repo in `~/Projects/ai/knowledge`, untracked — moved here so changes are reviewable.

## How these reach the agent

**Not automatically.** Nothing in this directory is read at runtime. These files are pasted
into Sentinel's own UI:

- `*_knowledge_base.md` → Knowledge Base editor
- `*_report_prompt.md` → the workflow's custom prompt

Edit here first, commit, then paste. A change that only exists in the UI will be lost the next
time someone works from this directory, and vice versa.

## The one exception

The platform activity digest prompt is **not** here. It lives at
`src/Sentinel.Api/Templates/platform-activity-prompt.md`, because `WorkflowDefaults` reads it
from disk to seed `seed-platform-activity-digest`. Keeping a second copy here is what caused
the two versions to drift apart once already — edit the Templates copy directly.

Note that the seeder only supplies the prompt when the workflow row is created. An existing row
keeps whatever prompt it has, so a change to that file also needs pasting into the UI for
workflows that already exist.
