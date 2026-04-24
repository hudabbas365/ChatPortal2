---
title: "Getting Started with AI Chat & Agents"
slug: "getting-started-ai-chat-agents"
author: "AIInsights365 Team"
sortOrder: 2
published: true
summary: "Create your first workspace, wire up a datasource, spin up an AI agent, and ask AIInsights365.net your first natural-language question in under five minutes."
---

# Getting Started with AI Chat & Agents

Welcome to **AIInsights365.net**. This guide walks you from zero to your first real analytics answer in about five minutes. By the end you will have created an organization, invited a teammate, built a workspace, connected a datasource, provisioned an AI agent, and asked a natural-language question that returns SQL, a chart, and narrative. Nothing in this guide requires prior experience with BI tools, SQL, or machine learning — although if you have that background you will discover plenty of power-user capabilities along the way.

We recommend keeping this document open in one browser tab while you work in another. Every section is written so that you can follow along step by step, with screenshots rendered in your head rather than on the page (we want the guide to stay fast to load). When you see a quoted natural-language prompt, feel free to type it into AIInsights365.net verbatim. The agent is tuned to handle phrasing variations gracefully.

## Before You Begin

AIInsights365.net is a hosted SaaS, so there is nothing to install. You will need:

1. A modern browser — Chrome, Edge, Firefox, or Safari. Internet Explorer is not supported.
2. A work email address that can receive confirmation mail.
3. Read access to **at least one** database or CSV file you want to analyze. If you don't have one handy, AIInsights365.net ships with a sample datasource you can clone with a single click.

If your data lives behind a corporate firewall and you do not yet have an outbound-friendly connection, skip this guide and read the Phase 2 post on the On-Prem Data Gateway. Phase 1 supports direct connections to internet-reachable databases; Phase 2 bridges the private network case.

## Step 1 — Create Your Organization

When you sign up at `https://aiinsights365.net/auth/signup`, you are the founding admin of a new **Organization**. An organization is the top-level tenant boundary in AIInsights365.net. Everything else — users, workspaces, agents, datasources, billing, notifications — lives inside an organization and is isolated from every other organization on the platform.

During sign-up you provide your company name, your full name, and a password. You will receive a confirmation email within a few seconds; click the link to verify your organization email address. Verification is how we know the domain you signed up with really belongs to you. Until you verify, certain capabilities are disabled: you cannot send broadcast notifications, buy licenses, or upgrade past the free trial.

After verification you land on the dashboard. The first thing you will notice is the top-right bell icon. Click it — you should see at least one system notification welcoming you and reminding you that your 30-day free trial has begun. Notifications are how AIInsights365.net surfaces important state changes: trial countdowns, billing events, security alerts, and org-admin broadcasts all flow through the same bell. The count badge updates every thirty seconds; the dropdown scales from zero to ninety-nine and shows a compact `99+` for anything larger.

## Step 2 — Invite Your Team (Optional)

AIInsights365.net becomes exponentially more useful when multiple teammates share a workspace. To invite collaborators, open **Org Settings → Users**, click **Create User**, and enter their email, full name, and role. Roles are:

- **User** — can participate in workspaces they are added to.
- **OrgAdmin** — can manage workspaces, users, licenses, billing, and notifications for the entire organization.
- **SuperAdmin** — reserved for AIInsights365.net operators; not applicable here.

You can skip this step and explore the platform solo. You can also invite people later; everything you create now will remain yours when your team joins.

## Step 3 — Create a Workspace

A **workspace** is where data, agents, and insights live. Think of it as the analytics equivalent of a Slack channel: scoped to a domain (sales, finance, product, ops), with its own membership and content. Go to **Workspaces → New**, give it a name like *"Sales Pulse"* or *"Finance Q4"*, and pick a theme color. Workspaces with different colors are much easier to scan in the sidebar once you have more than three.

Workspace membership is separate from organization membership. You can belong to the organization without belonging to a given workspace — and workspace roles (Viewer, Editor, Admin) are distinct from your org role. This gives Org Admins the flexibility to carve up data access cleanly: the sales ops team gets a Sales workspace, the finance team gets a Finance workspace, and people can only see what they have been invited into.

## Step 4 — Connect a Datasource

With your workspace created, click **Datasources → New**. Phase 1 supports:

- **SQL Server** (both cloud Azure SQL and on-prem via direct network reachability)
- **PostgreSQL** (including managed Postgres on every major cloud)
- **MySQL / MariaDB**
- **SQLite** (ideal for prototypes and demos)
- **CSV upload** (for quick one-off analysis)
- **REST API / GraphQL** (limited — full coverage lands in Phase 2)

Pick the one that matches your data. If you selected a network database, enter the host, port, database name, and credentials. AIInsights365.net stores credentials encrypted at rest with per-tenant keys and opens a connection on your behalf when a query runs. We never store raw query results permanently; only the metadata needed to explain what happened (query, duration, row count) is retained.

After you save, AIInsights365.net runs an automatic **schema import**. This is a critical step: the platform walks every table, column, foreign key, and sample value, and it stores the resulting metadata in a structured form the AI can reason over. Schema import typically completes in seconds for a small database and in a couple of minutes for a warehouse with thousands of tables.

If the import flags relationships it could not auto-detect, you can add hints manually. Hints are short sentences like *"OrdersHeader.CustomerId joins to Customers.Id"* that the agent reads alongside the schema. Good hints are one of the highest-leverage things you can do to make AIInsights365.net accurate on your specific data.

## Step 5 — Provision an AI Agent

A **datasource** is dumb — it just knows how to run SQL. An **agent** is smart — it knows how to understand your question, pick the right tables, write SQL, and explain the result. Click **Agents → New** in your workspace and pick the datasource you just connected. Give the agent a name (something evocative like *"Sales Analyst"*) and a personality.

AIInsights365.net ships with three built-in agent archetypes:

1. **Analyst** — concise, SQL-heavy, assumes a technical audience. Best for power users.
2. **Narrator** — long-form, narrative, explains findings in plain English. Best for reports.
3. **Power User** — adaptive; behaves like Analyst for precise questions and like Narrator for open-ended ones.

Each archetype is defined by a system prompt template which you can view and modify. If you have in-house terminology — product codes, team names, KPI definitions — you should paste a short glossary into the agent's custom notes. The agent will reference the glossary on every question, which dramatically improves relevance.

The final knob is **temperature**. Lower temperatures (0.1–0.3) make the agent deterministic and conservative — ideal for finance and compliance contexts. Higher temperatures (0.6–0.9) encourage creativity and variety — better for exploratory research. We default to 0.3, which works for most teams.

## Step 6 — Ask Your First Question

Open the chat panel on your agent and type a question. Try one of these:

> "Show me revenue by region for the last 6 months as a stacked bar chart."
>
> "Which 10 customers have churned in the past 90 days, and what is their total lifetime spend?"
>
> "Plot weekly active users over the last year and flag any weeks below the rolling 4-week average."

You will see the agent stream three things in parallel:

1. **SQL** — the query it plans to run, shown in a fenced code block with a copy button.
2. **Result** — the chart or table, rendered as soon as the query returns.
3. **Narrative** — a short written explanation of what the data says, including any caveats.

If the agent's first attempt isn't quite right, refine in plain English. Say *"Break it down by channel instead"* or *"Use a heatmap"* or *"Limit to North America."* The agent treats the entire conversation as context, so follow-ups are cheap and fast.

## Step 7 — Pin, Share, and Schedule

Once you have an answer you like, click **Pin**. Pinning adds the result as a tile on the workspace dashboard. Tiles can be resized, rearranged, and themed. Dashboards are fully responsive; they look right on a laptop, a 4K monitor, or a tablet during a meeting.

To share a result with someone outside AIInsights365.net — say, an auditor or a board member who doesn't have a seat — click **Share Link**. The platform generates a tokenized, expiring URL that renders the chart and narrative without exposing credentials or the underlying SQL. Tokens can be revoked at any time from the share management page.

Scheduling is coming in Phase 2, but in Phase 1 you can already subscribe yourself or teammates to **dashboard snapshots**. A snapshot captures the dashboard as it is right now, attaches it to an email, and delivers it on a cadence you choose.

## Step 8 — Explore the Q&A Floating Panel

While viewing a pinned chart or an auto-generated report, look for the small floating FAB in the bottom-right corner. That is the **Q&A panel**. Click it, ask a question about the artifact you are looking at, and watch the streaming answer appear alongside the original content. The panel keeps its own conversation history per artifact, so you can come back tomorrow and pick up where you left off.

If you collapse the panel, it turns into a small bell-style icon. Drag it anywhere on the screen; its position persists per user per artifact.

## Troubleshooting

If your first question returns an error, the cause is almost always one of three things:

1. **The datasource is unreachable.** Check that the host/port you entered is correct and that outbound network access from the AIInsights365.net cloud is permitted by your firewall.
2. **The agent lacks context.** Add schema hints, custom notes, or a short glossary. Re-ask the question.
3. **The query exceeded the row-count cap.** Phase 1 caps single-query result sets at 100,000 rows by default. Ask the agent to aggregate or paginate.

For anything stubborn, open the activity log from the Org Admin portal and share the failed execution ID with support. Every execution is traceable end-to-end.

## What Comes Next

You have completed the five-minute onboarding. From here, we recommend:

- Read **Chart Library — 72+ Visualizations** to learn the chart taxonomy.
- Read **Reports, Dashboards & the AI Q&A Panel** to get the most out of Phase 1's report authoring.
- Try the **Auto Report Generator** on any agent — it is genuinely magical the first time.
- Set a **token budget** in the Org Admin portal so you can experiment freely without worrying about cost.

Welcome to AIInsights365.net. We think you will find that analytics feels fundamentally different when the interface is a conversation and the product is built by people who believe AI should be a partner, not a gimmick.
