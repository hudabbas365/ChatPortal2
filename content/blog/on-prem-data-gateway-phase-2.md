---
title: "The On-Prem Data Gateway — Phase 2 Deep Dive"
slug: "on-prem-data-gateway-phase-2"
author: "AIInsights365 Team"
publishedAt: "2025-04-22"
published: true
summary: "How the AIInsights365.net Gateway securely bridges cloud AI to on-premises SQL Server, Oracle, SAP, and file shares — without opening a single inbound firewall port."
---

# The On-Prem Data Gateway — Phase 2 Deep Dive

Every conversation we have with a customer about Phase 2 of **AIInsights365.net** eventually arrives at the same sentence: *"Most of our data lives behind the firewall."* This post is for the people who say that sentence. It explains in detail how the **AIInsights365.net On-Prem Data Gateway** works, why we designed it the way we did, what it does and does not do, and how your security team should think about it.

The short version: the gateway is a lightweight service you install inside your network. It opens an **outbound-only** TLS tunnel to AIInsights365.net, authenticates itself with a per-tenant key pair, and relays queries from our cloud AI to your private data stores. Credentials for on-prem systems never leave your datacenter. No inbound firewall ports need to be opened. All traffic is end-to-end encrypted, audit-logged, and subject to a policy engine you control. Think of it as a safe, one-way bridge that lets our AI see your data without letting anyone else see your network.

## The Problem the Gateway Solves

Most meaningful corporate data is not reachable from the public internet. It lives in Oracle databases behind VPN-only subnets, SAP HANA clusters in private datacenters, SQL Server instances on corporate LANs, CSV file shares on internal SMB mounts, MongoDB replica sets in isolated VPCs, and countless other flavors of "almost, but not quite, cloud." Analytics platforms that require network reachability to your data either ask you to expose it to the internet (a non-starter for most security teams) or ask you to run their entire stack on-prem (a non-starter for most finance teams).

Phase 1 of AIInsights365.net chose a narrow path: we supported only network-reachable databases. That made the product immediately useful for cloud-native teams and frustrating for anyone with a significant on-prem footprint. Phase 2 closes that gap with the gateway, which we think is the most elegant solution to the problem currently shipped by any analytics platform.

## Architecture at a Glance

```
  [ AIInsights365.net Cloud ]         [ Customer Datacenter / VPC ]
           │                                      │
           │                                      │
      Control Plane   <──────── TLS 1.3 ────────>   Gateway Service
           │        (outbound-only, from customer)       │
           │                                      │
     Query Dispatcher                          Local Driver Pool
           │                                      │   │   │   │
           │                                  SQL Server │ SAP HANA
           │                                     Oracle  Files
           │
    Per-tenant credentials vault (never touches customer creds)
```

The flow for a single user question looks like this:

1. A user asks a natural-language question in AIInsights365.net.
2. The AI plans SQL (or the equivalent for the target datasource).
3. The query dispatcher in our cloud identifies the datasource as gateway-hosted and places a request on that tenant's gateway inbox.
4. The **customer-side** gateway service, which is already holding an outbound TLS connection to our cloud, receives the request over that connection.
5. The gateway authenticates the request (per-query signing), applies local policy (allow-list / deny-list / row-level rules), looks up the target datasource credentials from its local vault, executes the query, and streams the result back up the same TLS tunnel.
6. Our cloud receives the result, renders the chart, composes the narrative, and streams it back to the user.

Nothing in this flow opens an inbound port on the customer side. Nothing leaves the customer network except query results (and the gateway policy engine can redact or block results before they leave). The control plane never learns customer datasource credentials; those live exclusively in the local gateway vault.

## Why Outbound-Only

Network security architects have spent the last fifteen years arguing that inbound-connection models are the fundamental weakness in most enterprise integrations. A VPN tunnel. A reverse-proxy allow-list. An exposed database. Each of those is a potential point of ingress, and each requires ongoing care to keep safe.

Outbound-only tunnels reverse the posture. The customer initiates the connection; the cloud can only reach data if the customer's gateway is willing to fetch it. If the customer wants to instantly cut off all AIInsights365.net access, they stop the gateway process. That is it. No firewall rule to edit, no credential to rotate, no incident-response runbook — the connection simply ceases. Many security teams are comfortable with this model specifically because it is so easy to reason about.

We chose outbound-only because it is what we would want as customers. It is also what most of the customers we talked to insisted on. Shipping a gateway that required inbound connections would have been faster to build and dead on arrival.

## The Local Credential Vault

The gateway ships with a **local credential vault**. When an Org Admin registers a datasource that lives behind the gateway, the admin enters the credentials (SQL Server connection string, Oracle TNS entry, SAP HANA JDBC URL, SMB file path, etc.) into the gateway's local UI. The credentials are encrypted at rest using an **installation-specific key** and never transmitted to AIInsights365.net's cloud.

When the cloud needs to execute a query, the cloud does not send credentials. It sends the *target datasource identifier*, which the gateway resolves locally by looking up the corresponding credentials in its vault. The gateway uses those credentials to open a connection to the private data store, executes the query, and streams the rows back up the tunnel. From the cloud's point of view, the query returns rows; the cloud has no idea which username ran it or how the gateway authenticated.

This separation matters because it means a cloud compromise cannot, by itself, leak your on-prem credentials. The vault is a local artifact. Your security team controls its backup, rotation, and key management. We provide the tooling (hook into HashiCorp Vault, AWS Secrets Manager, Azure Key Vault, or a plain OS-level keystore, whichever you prefer), but the keys live with you.

## The Policy Engine

Not every query should run, even if it is syntactically valid. The gateway includes a **policy engine** that runs before each query and can modify, block, or log it. Policies are authored in a small, readable rule language. Examples:

```
deny:
  - table: employees
    column: ssn
  - table: salaries

redact:
  - table: customers
    column: email
    method: hash

allow_only_from_workspaces:
  - finance
  - compliance
```

Policies can be scoped per datasource, per workspace, per user, or globally. They are evaluated locally (on the gateway), so even a subtly malicious query from the cloud cannot bypass them. Every policy action is logged to the gateway's local audit trail in addition to the cloud's audit trail, giving you two independent records.

For organizations that already run a central data governance layer, the gateway's policy engine can be configured as a thin wrapper around that layer (calling out to Immuta, Collibra, Privacera, or a custom service). The gateway does not need to be the canonical source of policy; it only needs to respect it.

## High Availability

Organizations that depend on analytics for operational decisions cannot tolerate a single-host gateway. Phase 2 ships an **active/passive HA model** out of the box and an **active/active** model for customers on Enterprise tier.

In the active/passive model, two gateway instances register themselves with the cloud under the same tenant identifier. Only one holds the primary outbound connection at a time; the other holds a standby. If the primary misses health-checks, the cloud promotes the standby within a few seconds. Failover is transparent to the user; in-flight queries retry once, and the user sees at most a brief spinner.

In the active/active model, both instances hold live outbound connections and share query load via a consistent-hash dispatcher in the cloud. This adds capacity as well as resilience and is the recommended topology for high-throughput customers.

Both models support rolling upgrades. You can patch gateway binaries on one instance while the other continues serving queries; end users see no interruption.

## Supported Sources Behind the Gateway

In the Phase 2 launch, the gateway supports every datasource in the Phase 2 connector lineup that can run behind a firewall:

- **SQL Server** (all modern versions, Always On, Azure Arc-enabled)
- **PostgreSQL**
- **MySQL / MariaDB**
- **Oracle Database** (11g and newer)
- **SAP HANA**
- **MongoDB**
- **Elasticsearch / OpenSearch**
- **ClickHouse**
- **File shares** (SMB, NFS, local CSV and Parquet)

Cloud-native warehouses (Snowflake, BigQuery, Databricks, Redshift) generally do not need the gateway because they are reachable over public internet with proper IP allow-lists. If you do want to front them with the gateway anyway — for policy uniformity, for example — that is supported.

## Installation Footprint

The gateway is a single service, distributed as:

- A Windows MSI for on-prem Windows servers.
- A systemd-aware Linux binary for RHEL, Ubuntu, and Debian hosts.
- An OCI-compliant container image for Kubernetes and Docker Swarm environments.
- A Helm chart for Kubernetes native installs.

System requirements are modest. A single gateway instance comfortably handles a few hundred concurrent queries on 2 vCPU and 4 GB of RAM. Typical enterprise deployments sit at two or three instances for HA and room to grow. The gateway requires outbound TCP 443 to AIInsights365.net; no other network access.

The local UI exposes a small management interface on a loopback port by default. For organizations that want centralized management, the gateway also supports configuration via files and environment variables, making it first-class for GitOps workflows.

## Observability

The gateway emits structured logs and OpenTelemetry metrics. Every query executed through it is logged with:

- Request correlation ID (same one that appears in the cloud audit log, for cross-referencing).
- Initiating user and workspace.
- Target datasource and resolved policy decisions.
- Query text (optionally redacted per your policy).
- Row count, runtime, and status.

The metrics plug into Prometheus, Datadog, New Relic, Splunk, Elastic, or any OTLP-compatible backend. Grafana dashboards ship in a community Git repository we maintain. Alerting templates are provided for the handful of conditions you actually care about: gateway offline, elevated error rate, abnormal query rate, vault integrity failure.

## Security Review Kit

Enterprise customers invariably need to put the gateway through a formal security review. We ship a **Security Review Kit** that includes:

- The gateway's full source code review for the security-sensitive components (under NDA).
- A threat model document covering every trust boundary.
- A third-party penetration test report, refreshed annually.
- SOC 2 Type II and ISO 27001 attestations covering the cloud side.
- A questionnaire-response bundle in standard formats (SIG Lite, CAIQ).

The goal of the kit is to compress your review from months to weeks. We have been through enough reviews to know exactly what reviewers will ask; the kit answers most of it up front.

## What the Gateway Does Not Do

For completeness, a few things the gateway deliberately does *not* do:

- It does not tunnel arbitrary TCP traffic. It is a purpose-built query relay, not a general network bridge.
- It does not store query results persistently. Results stream through and are dropped.
- It does not expose a UI to customer end-users. Business users interact only with AIInsights365.net; the gateway is an infrastructure component that IT operates.
- It does not execute AI models locally. All AI inference still happens in the AIInsights365.net cloud (the cloud sends down executable queries, not embeddings). If you need local model inference, that is a Phase 4 conversation.

## Rollout Plan and Pricing

The gateway enters **private preview with design partners** at the start of Phase 2 and hits **general availability** later in the phase, alongside the Oracle / SAP HANA connectors that benefit from it most. Enterprise tier includes the gateway at no additional cost and supports active/active HA. Professional tier includes the gateway at no additional cost and supports active/passive HA. There is no seat or query surcharge for gateway usage; it is part of the subscription.

## Get Started

If your organization has been waiting to adopt AIInsights365.net because of data residency or network-reachability concerns, the gateway is the answer. Reach out via the sales contact on our website to be added to the private preview list. We take waves of three to five organizations per month during preview, with a hands-on onboarding that typically takes under two weeks.

AI without access to your real data is a novelty. The gateway is how we ensure AIInsights365.net works with the data that actually drives your business, securely and with respect for the boundaries your security team has carefully constructed.
