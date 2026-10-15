# AI agent onboarding

Every AI agent (Claude, Codex, Copilot, anything) working in this repository starts a session with these steps, before touching code. The person you work for decides what you do; this only tells you where you stand.

## 1. Find out who you are working for

Run, in this order, and stop at the first that gives a name:

```bash
gh api user --jq .login
git config user.name
git config user.email
```

Match the result (case-insensitive) against the handles in [ROLES.md](ROLES.md). Known aliases: `SeungKyuHan` = `lollipop190000`, `srchae99` = `1213anthony` (the second is unconfirmed). If nothing matches or the commands fail, **ask the user which handle is theirs**. Do not guess, and do not assume the user is the person who last committed.

## 2. Tell the user, in five lines or fewer

Start your first reply of the session with:

1. **Your role**: their area(s) from ROLES.md, or "you have no role yet" with the list of open areas.
2. **Your paths**: what they own and what needs the owner's approval first.
3. **Right now**: the unchecked ROADMAP items for their area, and the current milestone.
4. **Open decisions** that affect their area (status Proposed in [docs/decisions](../decisions/README.md)).
5. **Anything changed since their last session**: run `git log --oneline -15 origin/main` and mention merges in their area.

Then ask what they want to do. Do not start work unprompted.

## 3. Rules you carry for the whole session

Read [AGENTS.md](../../AGENTS.md) and [WORKFLOW.md](WORKFLOW.md). In short: work on an `<area>/<name>` branch, stay inside the user's area unless told otherwise, never push to `main`, never merge, never edit the main scene, `Packages/` or `ProjectSettings/` unless the user is the Core owner, add a licence row for any asset, and report what you did not test.

If the user asks for something in another person's area, say whose it is and suggest an issue or a ping instead of editing it silently.

## 4. Keep this file honest

If ROLES.md and reality disagree (someone left, a role changed), tell the user and propose a ROLES.md change as a PR. Do not edit roles on your own.
