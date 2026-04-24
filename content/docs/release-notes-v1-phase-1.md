---
title: "Release Notes — AIInsights365.net v1.0 (Phase 1)"
slug: "release-notes-v1-phase-1"
author: "AIInsights365 Team"
sortOrder: 1
published: true
summary: "Everything that shipped in the first public release of AIInsights365.net — AI-powered chat, 72+ chart types, agent workspaces, auto reports, token budgeting, and a content-rich SEO engine."
---

# Release Notes — AIInsights365.net v1.0 (Phase 1)

We are proud to announce the general availability of **AIInsights365.net v1.0**, the first public milestone of an AI-first analytics platform that we have been designing, building, refining, and battle-testing for the better part of a year. This release is what we call **Phase 1**: the foundation upon which every future Phase 2, 3, and 4 capability will be built. It is also the moment when AIInsights365.net transitions from a private technology preview into a product that organizations can adopt, integrate, and rely on for day-to-day decision making.

The guiding philosophy of AIInsights365.net is simple to state and surprisingly hard to execute: **put artificial intelligence at the center of the analytics experience, not at its periphery.** Nearly every competing tool we have studied was originally built as a dashboard editor — a canvas on which a human analyst drags fields, drops charts, and writes SQL. Over the last two years those same tools have glued an AI assistant onto their left rail, often little more than a text box that returns a chart suggestion. AIInsights365.net takes the opposite approach. The AI is the product. Charts, dashboards, and reports are artifacts that fall out naturally from a conversation with a schema-aware agent.

This release embodies that philosophy from top to bottom. Every major feature in Phase 1 was designed around a single question: *"What is the most natural way for a human being to get an answer from data?"* The answer, we believe, is to talk to it — and to have the data talk back with charts, narrative, and cited SQL you can audit. Phase 1 is our first-class implementation of that idea.

## Highlights at a Glance

- **Natural-language chat** over SQL Server, PostgreSQL, MySQL, SQLite, and CSV uploads.
- **AI agents** scoped to a datasource and a workspace, with schema awareness and memory.
- **72+ interactive chart types** rendered on a unified canvas pipeline.
- **Auto Report Generator** that drafts multi-page reports from a single prompt.
- **Q&A floating panel** on every report, with streaming answers and a draggable FAB.
- **Dashboards** with pinned tiles, layout memory, and organization-level theming.
- **Organization and Super-Admin portals** with RBAC, trial enforcement, and audit logging.
- **Token budgeting** so AI spend is predictable at enterprise scale.
- **PayPal billing** with a 30-day free trial, monthly subscriptions, license management, and token add-ons.
- **SEO engine** that indexes every page, doc, and blog post into `sitemap.xml`.

Each of these deserves its own release note. Below we walk through the ones that will most shape the way your team works.

## AI Chat over Your Data

The core of AIInsights365.net is a conversational interface that accepts natural-language questions and returns SQL, charts, narrative, and follow-up suggestions. In Phase 1, that interface supports both one-shot questions and multi-turn conversations; the agent remembers the thread, refines queries as you refine the question, and preserves context across dashboard reloads.

Under the hood, every question flows through a schema-aware planner that inspects your datasource metadata, decides which tables participate, synthesizes SQL, executes it with row-count and runtime guardrails, and streams the result to the UI. The agent also cites the SQL it ran, so you can verify, copy, or adapt it. We believe trust in AI-generated analytics starts with transparency, and Phase 1 enforces that at every step.

Performance was a first-class concern. The chat stream is rendered token-by-token, charts render progressively, and large result sets are paginated at the source. Even on modest SQL Server instances, typical questions return answers in two to four seconds end-to-end.

## Workspaces and Agents

A workspace is AIInsights365.net's unit of collaboration. Inside a workspace you can create datasources, configure AI agents, save chat threads, pin chart outputs, and publish reports. Access is governed by workspace membership roles: Viewer, Editor, and Admin. A single organization may create an unlimited number of workspaces on Professional and Enterprise tiers, letting teams keep sales data, finance data, product telemetry, and customer-support signals cleanly separated.

Agents sit one level below the workspace. An agent is an AI personality bound to a specific datasource, with its own prompt template, temperature, model choice, and optional few-shot examples. Phase 1 ships with three built-in agent archetypes — *Analyst*, *Narrator*, and *Power User* — and lets organization admins clone or customize any of them. Every agent execution is logged with timestamp, user, tokens consumed, and the SQL produced, so your compliance and cost-governance teams always have a complete audit trail.

## 72+ Chart Types on One Pipeline

Phase 1 ships with a unified chart renderer that covers the full analytics spectrum: comparison charts (bar, column, grouped, stacked, percent-stacked), trend charts (line, area, smooth, step, spline, candlestick), composition charts (pie, donut, sunburst, treemap, waterfall), distribution charts (histogram, box, violin, scatter, bubble), flow charts (sankey, chord, network, funnel), geospatial charts (choropleth, bubble maps, heat maps), and KPI tiles (gauge, bullet, scorecard, sparkline). All of them share one canvas pipeline, one theming engine, one export path (PNG, SVG, CSV, JSON), and one accessibility layer.

The AI chooses the chart for you by default. If your question involves a time dimension and a single measure, the agent picks a line chart. If it involves two dimensions and a single measure, it picks a grouped bar chart or a heatmap, depending on the number of distinct values. If your question compares parts of a whole, the agent reaches for a donut or a treemap. You can override any of these decisions with a single word in the follow-up — "Make it a sankey" — and the chart reshapes instantly.

## Auto Report Generator

Single-question analytics solve one problem; generating a full, multi-page report that survives executive scrutiny is a bigger one. Phase 1 introduces the **Auto Report Generator**, a capability that accepts a single prompt such as *"Quarterly sales review with regional breakdown and anomaly callouts"* and produces a complete report with a table of contents, narrative sections, embedded charts, and data-backed findings. The generator uses an internal planner-executor loop: first it drafts a section outline, then it executes the queries needed for each section, then it composes narrative prose with inline chart references, and finally it compiles the artifact into a shareable HTML document.

Every generated report is **editable**. You can modify narrative text, regenerate individual sections, swap charts, or let the AI recompose the entire report against a fresh date range. Reports also carry the floating Q&A panel, so any stakeholder reading the report can ask follow-up questions against the same underlying data.

## The Q&A Floating Panel

One of the most user-visible additions in Phase 1 is the **draggable Q&A panel**. Every chart, dashboard, and report in AIInsights365.net now carries a floating assistant with a chat input. The panel streams responses, supports SQL citations, and can be dragged anywhere on the page. For accessibility, it can be pinned to the right rail, minimized to a FAB, or temporarily hidden; its position is remembered per user.

This matters because it collapses the usual "find an analyst, file a ticket, wait three days" loop. If an executive reading a quarterly report wants to know why Europe dipped in May, they ask the panel. The answer appears in seconds, rendered alongside the original report — no context switch, no ticket.

## Organization and Super-Admin Portals

AIInsights365.net ships with two privileged portals. The **Organization Admin** portal gives every organization full control of its users, workspaces, licenses, billing, compliance posture, and activity log. The **Super-Admin** portal, scoped to AIInsights365.net operators, provides a cross-tenant view of subscriptions, seat usage, broadcast notifications, and system notifications. Both portals are built on the same RBAC foundation, and every privileged action is written to the `ActivityLog` table with full context.

Role-based access control is enforced at three layers in Phase 1: controller-level authorization attributes, workspace membership checks in the `WorkspacePermissionService`, and row-level filters in the data-access layer. We ran extensive red-team exercises to ensure a user cannot escalate across organizations, workspaces, or datasources — and we commit to keeping that posture as Phase 2 adds more connectors and a data gateway.

## Token Budgeting and Billing

AI cost is the single question most enterprise buyers ask when they first evaluate a conversational analytics platform. Phase 1 answers it with a **token budget** per organization, a live usage meter in the Org Admin portal, and **token pack add-ons** purchasable through PayPal. Organizations can set alerts at 50%, 80%, and 100% of budget; once the cap is reached, AI-heavy features gracefully degrade instead of throwing errors.

Billing itself is built on PayPal Subscriptions and Orders. Phase 1 supports a 30-day free trial, Professional and Enterprise monthly plans, per-seat licensing, and one-time token pack purchases. Every payment writes a `PaymentRecord` and fires an organization-scoped notification, so admins always know the state of their account.

## SEO Engine

Finally, Phase 1 ships an **SEO engine** that treats every piece of public content — the marketing pages, the docs, the blog — as a first-class record in the database. The `SeoEntry` table stores meta titles, descriptions, keywords, Open Graph tags, sitemap priority, and change frequency. A background process generates `sitemap.xml` and `robots.txt` automatically. Even release notes like the one you are reading are indexed with a dedicated URL, canonical tag, and Open Graph preview.

This is especially important for an AI-first platform: the more richly we describe what we do, the easier it is for AI-powered search engines to surface AIInsights365.net to the people who would benefit from it.

## Known Limitations

We will not pretend v1.0 is feature-complete. Phase 1 intentionally ships a focused set of capabilities so we could nail their quality before expanding the surface area. Known limitations include: no on-premises data gateway yet (Phase 2), no Snowflake/BigQuery/Databricks connectors yet (Phase 2), no mobile-native apps (Phase 3), and limited offline support. We also deliberately shipped with English-only natural language; multilingual is on the Phase 2 shortlist.

## What Phase 2 Will Bring

Phase 2 focuses on **reach**. Fifteen new production-grade datasource connectors, an **On-Prem Data Gateway** for secure access to behind-the-firewall systems, an expanded chart library with geospatial improvements, multilingual support, and deeper AI model choices (including BYOM — bring your own model). You can read the full roadmap in the companion blog post, *"Phase 2 Roadmap — 15 New Datasources Coming to AIInsights365.net."*

## Thank You

None of this would have shipped without the dozens of design partners who tolerated rough edges, filed thoughtful bugs, and challenged us on everything from agent prompt quality to chart color ramps. If you are reading this and you were one of them: thank you. Phase 1 carries your fingerprints.

If you are new to AIInsights365.net, welcome. The rest of the documentation walks you through your first workspace, your first agent, and your first question — usually all in under five minutes. We cannot wait to see what you build.
