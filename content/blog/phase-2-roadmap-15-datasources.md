---
title: "Phase 2 Roadmap — 15 New Datasources Coming to AIInsights365.net"
slug: "phase-2-roadmap-15-datasources"
author: "AIInsights365 Team"
publishedAt: "2025-04-20"
published: true
summary: "Snowflake, BigQuery, Databricks, Redshift, Oracle, SAP HANA, MongoDB, Elasticsearch, ClickHouse, Salesforce, HubSpot, Google Analytics, Stripe, Shopify, and REST — the full Phase 2 connector lineup for AIInsights365.net."
---

# Phase 2 Roadmap — 15 New Datasources Coming to AIInsights365.net

Phase 1 of **AIInsights365.net** shipped with a tight, opinionated set of datasource connectors: SQL Server, PostgreSQL, MySQL, SQLite, CSV upload, and a limited REST client. That list was chosen deliberately — enough coverage to prove the AI-first thesis without drowning us in connector maintenance before the product's core was ready. Phase 1 is now ready. It is time to open the floodgates.

**Phase 2 adds fifteen production-grade datasource connectors**, taking AIInsights365.net from "interesting for teams on standard SQL" to "broadly useful across the modern data stack." This post explains the lineup, why we picked these fifteen specifically, how the AI stays smart across wildly different data shapes, and what to expect during the Phase 2 rollout.

## The Phase 2 Connector Lineup

Here is the full list, in roughly the order we plan to ship them:

1. **Snowflake**
2. **Google BigQuery**
3. **Databricks** (Delta Lake / Unity Catalog)
4. **Amazon Redshift**
5. **Oracle Database**
6. **SAP HANA**
7. **MongoDB**
8. **Elasticsearch / OpenSearch**
9. **ClickHouse**
10. **Salesforce**
11. **HubSpot**
12. **Google Analytics 4**
13. **Stripe**
14. **Shopify**
15. **Generic REST / GraphQL**

That list covers four very different categories of data: **cloud data warehouses** (Snowflake, BigQuery, Databricks, Redshift), **traditional databases** (Oracle, SAP HANA), **operational stores** (MongoDB, Elasticsearch, ClickHouse), and **SaaS application data** (Salesforce, HubSpot, GA4, Stripe, Shopify, plus the escape hatch of REST/GraphQL). Together, they represent roughly 90% of where business-critical data actually lives in a modern organization.

## Why These Fifteen?

We started Phase 2 planning with a long list — seventy-plus possible connectors — and whittled it down using three criteria.

### Criterion 1: Coverage Per Organization

Which connectors, if shipped, would let the maximum number of organizations run AIInsights365.net as their primary analytics interface without a bridge? We pulled anonymized data from the private preview and interviewed forty customers. The answer converged quickly on the top ten you see above. A typical mid-market organization has one data warehouse (Snowflake or BigQuery most commonly), one core CRM (Salesforce or HubSpot), one or two product-analytics sources (GA4, sometimes Mixpanel), and a payment processor (Stripe or Shopify or both). The fifteen we picked cover that pattern for the vast majority of customers.

### Criterion 2: AI Suitability

Not every datasource is a great fit for a conversational AI interface. Some are too schemaless to reason about; some are so rigid the AI adds little over a classic query builder. The fifteen connectors in Phase 2 all land in the sweet spot: they have enough schema metadata for an agent to understand the data, and enough breadth for the AI to add real value. We explicitly deprioritized sources where the AI cannot do a credibly better job than a well-designed dashboard — those will come later, once we have AI capabilities strong enough to justify their inclusion.

### Criterion 3: Safety and Isolation

Connectors vary enormously in how easy they are to secure. The fifteen we chose all support row-level or column-level permissions natively, OAuth or service-account authentication, and granular API scopes. That lets us build per-tenant credential vaults and least-privilege access from day one, rather than retrofitting security later. For customers in regulated industries, this is the critical point.

## Architectural Changes Required

Adding fifteen connectors is not a matter of bolting fifteen drivers onto Phase 1's database client. Several core platform pieces needed to evolve.

### A Unified Schema Model

Phase 1 stored schema metadata in a form that assumed relational data: tables, columns, foreign keys. Phase 2 introduces a **unified schema model** that handles relational tables, document collections, index mappings, and API resource graphs with the same primitives: *entities*, *fields*, *relationships*, *measures*, *dimensions*. Every connector translates its native schema into this model on import. The AI then reasons against the unified model, not the native model, which keeps the prompt engineering manageable and lets the same agent answer questions across very different source shapes.

### Semantic Hints Per Connector

SaaS connectors in particular carry a lot of implicit semantics. Every Stripe account has the concept of an "active subscription"; every Salesforce instance has a concept of a "closed-won opportunity." These are not things the AI should have to relearn per organization. Phase 2 ships with a **semantic hint library** per connector: a curated set of calculations, filters, and business concepts that the AI applies automatically when you connect that source. Organizations can override and extend the hints; the defaults are good enough to make the first question productive.

### Push-down vs. Pull-up Query Planning

For a warehouse connector like Snowflake, the right strategy is to push the entire query down to the warehouse and receive aggregated results. For a document store like MongoDB, the right strategy is to execute a lightweight query server-side and post-process client-side. For a SaaS API like HubSpot, the right strategy is to fetch pages of records with careful cache-awareness and never ask the API to do analytics at all. Phase 2 introduces a **planner** that picks the right strategy per connector and per question, transparently. Users do not see this complexity; they just see that AIInsights365.net is fast regardless of what is behind the curtain.

### Connector Health and Reliability

A fifteen-connector future is only credible if no single broken connector can take down the rest. Phase 2 isolates connectors in their own worker processes, with circuit breakers, retry policies, and health-check dashboards visible in the Org Admin portal. A degraded Salesforce API will not block your Snowflake questions. A slow MongoDB shard will not back up the chat stream.

## Spotlight: The Warehouse Quartet

Snowflake, BigQuery, Databricks, and Redshift are the four cloud warehouses most likely to be the "single source of truth" for a mid-market or enterprise organization. They are also architecturally different enough that a one-size-fits-all connector will not do them justice.

For **Snowflake**, we support OAuth and key-pair auth, respect roles and warehouses (the AI picks the right warehouse based on query cost), and emit Snowflake-native window functions whenever useful. For **BigQuery**, we respect project/dataset separation, use service account impersonation, and honor BI Engine where available. For **Databricks**, we integrate with Unity Catalog, so column-level lineage and permissions flow into AIInsights365.net unchanged. For **Redshift**, we support IAM authentication, materialized views, and Redshift Spectrum for querying S3.

Each warehouse connector benefits from the unified schema model *and* keeps the native features that make it worth using in the first place.

## Spotlight: The SaaS Six

SaaS connectors are where conversational AI shines brightest. A question like *"Show me MRR contribution by Salesforce pipeline stage for the last four quarters, sliced by account owner"* involves joining two or three Salesforce objects, applying currency normalization, and handling the messy edges of a sales pipeline. In a traditional BI tool, that is an afternoon of work. In AIInsights365.net Phase 2, it is a sentence.

- **Salesforce.** OAuth; respects sharing rules; understands leads, opportunities, accounts, contacts, cases, campaigns, and custom objects.
- **HubSpot.** OAuth; covers contacts, companies, deals, tickets, and marketing emails.
- **Google Analytics 4.** Service account; reads dimensions and metrics with automatic sampling awareness.
- **Stripe.** Restricted API keys; understands subscriptions, invoices, charges, refunds, and disputes.
- **Shopify.** OAuth; covers orders, customers, products, inventory, and abandoned checkouts.
- **REST / GraphQL.** A generic client for everything else, with request templating, pagination handlers, and OpenAPI import.

The Phase 2 semantic hint library is especially dense for these six. Out of the box, questions about MRR, ARR, pipeline conversion, funnel drop-off, and revenue recognition Just Work.

## Timeline and Sequencing

We will ship Phase 2 connectors in three waves:

- **Wave 1 (first two months of Phase 2):** Snowflake, BigQuery, Databricks, Redshift, MongoDB. These are the connectors with the highest demand and the most homogeneous architecture; grouping them lets us ship the unified schema model and planner in a single push.
- **Wave 2 (months three and four):** Oracle, SAP HANA, Elasticsearch, ClickHouse. These are more specialized connectors that benefit from the wave-one foundation.
- **Wave 3 (months five and six):** Salesforce, HubSpot, Google Analytics 4, Stripe, Shopify, REST/GraphQL. SaaS connectors go last because they benefit most from the semantic hint library, which we finalize during waves one and two.

Each wave is incremental: customers on Phase 1 get wave one as soon as it is ready, without waiting for waves two and three. Billing stays the same; the new connectors are included at the Professional and Enterprise tiers at no additional cost.

## What Stays the Same

Phase 2 adds a lot. It also deliberately does not change several things that Phase 1 got right.

- **The chat interface stays the same.** Ask a natural-language question; get SQL, a chart, and narrative back. You should not have to think about which connector is behind the question; the platform does that for you.
- **Agents stay the same.** An agent bound to Snowflake works like an agent bound to MongoDB. Phase 2 expands *what* agents can see; it does not change *how* you work with them.
- **Dashboards stay the same.** Cross-connector dashboards are a first-class citizen in Phase 2 — you can have a Snowflake tile and a Salesforce tile side by side — but the dashboard UX you already know continues to apply.
- **Security stays at the top of the list.** Every new connector ships with the same rigorous credential isolation, row-level checks, and audit logging as the Phase 1 set. We would rather delay a connector than ship it insecurely.

## What Comes After Phase 2

The obvious question is: *"Why not thirty connectors? Why not fifty?"* The answer is that each connector deserves care, and shipping badly integrated connectors is worse than not shipping them at all. Beyond the fifteen above, we expect Phase 3 to add around eight more (the major cloud object stores, a few specialized event streams, and a handful of enterprise-grade SaaS tools), plus deeper AI capabilities that will let us credibly support more schemaless and streaming sources.

We also expect Phase 3 to ship a **community connector SDK**, so customers and partners can build their own connectors against our unified schema model. That will matter for the long tail of internal-only systems that every large organization has. It is a Phase 3 item because we want the unified schema model and planner to be rock-solid first, based on the fifteen production connectors of Phase 2.

## Let Us Know What You Need

If your organization's critical datasource is not on the Phase 2 list, please tell us. The sales contact on our website reaches product directly. We have already expanded the list once based on customer feedback (we originally did not have ClickHouse on it), and we will do it again if there is clear demand. The roadmap is a living document; every conversation with a customer helps us make it better.

Phase 2 is the moment AIInsights365.net becomes usable for *your* organization, not just for the narrow band of teams that happen to live entirely on vanilla SQL. We have never been more excited to ship.
