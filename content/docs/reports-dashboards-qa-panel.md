---
title: "Reports, Dashboards & the AI Q&A Panel"
slug: "reports-dashboards-qa-panel"
author: "AIInsights365 Team"
sortOrder: 4
published: true
summary: "Auto-generate multi-page reports, pin charts to dashboards, and keep asking follow-up questions through the floating Q&A panel — all powered by AIInsights365.net AI."
---

# Reports, Dashboards & the AI Q&A Panel

Charts and chat threads are the atoms of AIInsights365.net. **Reports, dashboards, and the Q&A panel** are the molecules — the higher-level artifacts your team will actually share with stakeholders, review in meetings, and hand to executives who want answers without having to learn the product. This document covers all three, explains how they fit together, and shows why the AI-first design of AIInsights365.net changes the economics of authoring analytics artifacts.

The headline result is simple. In legacy BI tools, producing a polished report for an executive review is an eight-hour affair: you gather requirements, write SQL, build charts, lay out pages, write narrative, get review, and iterate. In AIInsights365.net, the same report takes eight minutes. The eight-hour version is the rare case where the executive needs something truly bespoke. The rest of the time, an AI-drafted report is better than a human-drafted one — because the AI is pedantic, consistent, and does not get bored halfway through paragraph four.

## The Three Artifacts

Before we go deep, a quick taxonomy.

- A **chart** is a single visualization bound to a single query. Charts live inside chat threads and inside other artifacts.
- A **report** is an ordered sequence of narrative sections, each of which may include charts, tables, KPIs, and supporting data. Reports are authored once, optionally regenerated, exported as HTML or PDF, and shared.
- A **dashboard** is an always-live collection of pinned charts arranged spatially on a canvas. Dashboards update automatically; reports are snapshots in time.

In short: **charts are units, reports are narratives, dashboards are cockpits.** All three carry the Q&A panel, and all three share the same security model.

## The Auto Report Generator

The flagship report authoring capability in Phase 1 is the **Auto Report Generator**. Click *New Report* in any workspace, pick an agent, and give it a prompt. The prompt can be a single sentence:

> *"Quarterly sales review with regional breakdown, anomaly callouts, and a forward-looking forecast."*

Or it can be a longer brief:

> *"Build the monthly leadership report. Start with a three-sentence executive summary. Include MRR, net new logos, churn, and CSAT as KPIs up top. Then break down MRR growth by segment (SMB, mid-market, enterprise) and by region. Call out any region that grew less than 2% or more than 20%. Close with a table of the top 10 deals won."*

When you click *Generate*, the Auto Report Generator runs a three-phase pipeline:

1. **Plan.** The agent reads your prompt, inspects the datasource schema, and drafts a section outline: titles, goals, and the queries each section will require. You can review the outline and regenerate if the structure is off.

2. **Execute.** For each section, the agent writes SQL, runs it under the usual row-count and runtime guardrails, and caches results. Query failures are logged and retried with a softened version of the plan; the agent never silently drops a section.

3. **Compose.** Narrative prose is generated against the cached results. The agent cites every number it mentions, so every claim in the report is linked back to the query that produced it. Embedded charts are picked by the same AI-selection logic documented in the Chart Library guide.

The whole pipeline typically completes in twenty to sixty seconds for a ten-section report. Longer reports with heavy data are linear: a thirty-section report takes about three times as long as a ten-section one.

### Editing Reports

Reports are not immutable. Every section has *Regenerate* and *Edit* controls. Regenerating re-runs the plan, execute, and compose phases for that section against the current data, preserving the section's position in the report. Editing drops you into a rich-text editor where you can rewrite the narrative, swap the chart type, or add custom commentary.

We intentionally kept manual editing light-touch. Heavy report-design surfaces tempt authors to spend forty minutes picking fonts instead of interrogating data. Our bet is that AI-generated narrative with a human sanity check is the right ratio for 95% of reports. For the 5% that genuinely need custom layout, Phase 2 will ship a free-form page designer.

### Scheduling and Delivery

Every report can be regenerated on a schedule (daily, weekly, monthly, quarterly) and delivered to an email distribution list. Scheduled regenerations inherit the original prompt, so as data changes the narrative changes with it — the May edition of *"Monthly leadership report"* reads differently from the April edition, because the underlying numbers are different.

Delivery is via email in Phase 1. Phase 2 adds Slack, Microsoft Teams, and webhook delivery. All delivery channels respect the same access controls: a report is delivered only to recipients who would be allowed to view the original report inside AIInsights365.net.

## Dashboards

Dashboards are spatial, always-live, and cheap to build. To create one, pin any chat response in any thread. The pinned chart becomes a tile on a fresh dashboard in the containing workspace; subsequent pins append tiles to the same dashboard by default. You can then drag, resize, group, and theme tiles to taste.

Phase 1 dashboards support the following capabilities:

- **Layout memory.** Your arrangement persists across reloads and across devices.
- **Cross-tile filters.** Drop a filter widget (date range, enum, search) and any tile tagged with the corresponding dimension refreshes automatically.
- **Tile drilldown.** Click any tile to open it full-screen, with its own Q&A panel and time machine (see below).
- **Theming.** Dashboards inherit the organization theme and can be overridden per-dashboard. Dark mode is a first-class citizen, not a retrofit.
- **Tokenized sharing.** Generate a share link with an expiration date and a passcode. The shared view is read-only and carries no credentials.

We care a lot about dashboards being **always fast**. Every tile lazy-loads on scroll, queries are deduplicated across tiles (two tiles asking for the same data execute one query), and results cache for the duration of the dashboard session. Typical dashboards feel as snappy as a native app; large dashboards (fifty tiles, multiple datasources) still render first-paint in under a second on a modest laptop.

### Dashboard Time Machine

A small but high-value Phase 1 feature is the **time machine**. Every dashboard can be rewound to any point in the last thirty days. The tiles re-run their queries with a historical snapshot timestamp, letting you see exactly what the dashboard looked like last Tuesday. This is invaluable when you are reviewing a past decision and need to reason about what data was available at the time.

The time machine works because AIInsights365.net maintains a lightweight query ledger per datasource, not because it keeps copies of your data. As long as the datasource itself has historical data (most do), the rewind is free.

## The Q&A Floating Panel

Every chart, every report, and every dashboard carries the **Q&A floating panel**, a draggable chat interface that opens to the right of whatever you are viewing. The panel is the reason reports and dashboards are not dead artifacts. A stakeholder who opens the monthly leadership report and spots a dip in enterprise bookings can ask, inline, *"Why did enterprise bookings dip in week 43?"* — and get a streaming answer alongside the original report.

The panel maintains a separate conversation history per artifact per user. You can close it, open it tomorrow, and resume where you left off. Other users viewing the same artifact have their own histories; nothing you ask is visible to them by default. Organization admins can opt-in to a **shared Q&A mode** that makes questions and answers visible to every viewer — useful for runbooks and playbooks where the institutional knowledge is the point.

### How the Panel Picks Context

Under the hood, the panel combines several layers of context into every prompt:

1. **The artifact.** A serialized representation of the chart, report, or dashboard you are viewing, including the queries that produced it.
2. **The agent.** If the artifact is bound to a specific agent, that agent's prompt template, glossary, and temperature are applied.
3. **The conversation history.** The N most recent questions and answers in the current panel thread.
4. **The organization knowledge base.** Any custom notes, FAQs, or data-dictionary entries the Org Admin has pinned to the workspace.

This layered context is why the panel feels "smart." It does not start every conversation from scratch; it starts from the specific artifact you are staring at, with the agent you (or your admin) chose for that workspace.

### Performance

The panel streams. Tokens appear within roughly one second of hitting *Send*, and complete answers arrive in roughly three to eight seconds for typical questions. If the agent decides it needs to run a new SQL query, you see the query plan in real time — and if the query is slow, the panel shows a progress indicator rather than hanging.

### Accessibility

The panel is keyboard-navigable. Press `Ctrl+Shift+Q` (or `Cmd+Shift+Q` on macOS) to focus it from anywhere. Tab order is sane; Escape collapses the panel; arrow keys browse previous questions. Screen reader users get ARIA live regions for streaming responses.

## Security and Access Control

Reports, dashboards, and the Q&A panel all inherit workspace-level access control. A user who can see the workspace can see the dashboard; a user who can see the dashboard can use its Q&A panel; a Q&A panel can only query datasources the user has permission to see.

Shared links expand the perimeter carefully. A tokenized share link can view exactly the frozen artifact it was generated against and nothing else. Its Q&A panel (if enabled) can answer questions that would be answerable from the shared artifact alone — no lateral access to the underlying datasource. Tokens can be rotated or revoked at any time.

Every access event is logged. The activity log records who viewed which dashboard when, who asked which question, and whose query produced which row count. This level of granularity is overkill for most teams and indispensable for the ones who need it (regulated industries, customer-data-bound workflows, internal audit).

## Putting It All Together

Here is the canonical workflow that emerges once a team has been on AIInsights365.net for a month or two:

1. Analysts explore in chat and pin the most useful charts to **workspace dashboards**.
2. Managers consume the dashboards and use the **Q&A panel** to probe anomalies.
3. Directors receive the monthly, AI-generated **report** in their inbox and use its embedded Q&A panel to dive into specifics before the next review meeting.
4. Executives rarely touch the UI. They read the report, skim the dashboard, and trust the RBAC, audit log, and token budget to keep the whole thing safe and economical.

This is what we mean by AI-first analytics. It is not that AI draws prettier charts (although it does). It is that the entire production-to-consumption lifecycle — from question to chart to dashboard to report to follow-up — is shortened by an order of magnitude because AI is woven into every layer.

Phase 2 sharpens this story with on-prem data sources, a free-form report designer, and deeper collaboration features (comments, mentions, annotations on specific chart points). If you have feedback on what would make reports and dashboards more useful for your team, your Org Admin has a *Submit Feedback* link in the top-level nav that goes straight to our product team. We read everything.
