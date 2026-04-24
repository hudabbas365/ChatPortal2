---
title: "Chart Library — 72+ Visualizations"
slug: "chart-library-overview"
author: "AIInsights365 Team"
sortOrder: 3
published: true
summary: "A tour of the enhanced chart library that ships with AIInsights365.net Phase 1, including the full taxonomy, AI-driven chart selection, performance guarantees, and accessibility commitments."
---

# Chart Library — 72+ Visualizations

Every analytics platform eventually makes a bet on visualization. Some bet on a single opinionated chart type and tell users to bend their questions to match it. Others bet on infinite configurability and leave users to navigate a maze of options. **AIInsights365.net Phase 1 bets on breadth with intelligence**: more than seventy-two chart types, all rendered through one unified canvas pipeline, and all selectable either by the AI on your behalf or by you in a single follow-up sentence.

This document is a guided tour of the library. It explains what each category of chart is for, when the AI will pick it automatically, how to override, and what performance and accessibility guarantees ship with every visualization. We have tried to keep the tour practical: if you are wondering "should I use a treemap or a sunburst," you will find an answer below.

## Why Seventy-Two?

The number is not arbitrary. It covers the full analytical palette that a modern data team can reasonably need, without inflating the count by adding cosmetic variants. We drew the line at chart types that answer a meaningfully different question. A stacked bar chart answers a different question from a percent-stacked bar chart; both are in. A bar chart with a drop shadow is not a different chart; it is a theme choice, and themes are handled separately.

The seventy-two types span seven categories: **comparison**, **trend**, **composition**, **distribution**, **flow**, **geospatial**, and **KPI**. We will walk through each.

## Comparison Charts

Comparison charts answer the question *"How does quantity X vary across category Y?"* They are the workhorses of analytics. AIInsights365.net ships bar, column, grouped bar, grouped column, stacked, percent-stacked, diverging, and lollipop variants. The AI will pick the right one based on the cardinality of Y and the relationship between the measures:

- **Single measure, 2–15 categories**: bar or column (vertical if Y is ordered, horizontal if Y has long labels).
- **Two measures, same categories**: grouped bar.
- **Parts of a whole across categories**: stacked bar if absolute scale matters, percent-stacked if the ratio matters.
- **Positive vs. negative contribution**: diverging bar.
- **Sparse comparison with emphasis**: lollipop.

Every comparison chart supports sorting by value, label, or a secondary measure. Click any axis label to sort; the chart reshuffles with an animated transition.

## Trend Charts

Trend charts answer *"How does X change over time?"* We ship line, multi-line, smooth line, step line, spline, area, stacked area, percent-stacked area, and candlestick. The AI picks based on the shape of the time series:

- **Single series, continuous**: line.
- **Multiple series, modest noise**: multi-line with hover tooltips.
- **Multiple series, high noise**: smooth line or spline.
- **Discrete state changes**: step line.
- **Cumulative totals over time**: stacked area.
- **OHLC / financial data**: candlestick.

Every trend chart supports brushing: click-and-drag on the time axis to zoom into a sub-range. Double-click to reset. On dashboards, brushing on one trend chart can propagate to peer charts via a workspace-level filter — great for "show me everything happening the week of May 14."

## Composition Charts

Composition charts answer *"What does X break down into?"* We ship pie, donut, sunburst, treemap, nested treemap, waterfall, and Marimekko.

A few rules of thumb:

- **3 or fewer slices**: pie or donut is clearest. Donut is strictly better than pie when you also want to show a KPI in the center.
- **4–8 slices, flat hierarchy**: donut with a legend.
- **Hierarchical breakdown (category → subcategory → item)**: sunburst if the hierarchy is balanced; treemap if it is skewed. Sunburst is prettier; treemap is more honest about proportions.
- **Changes that add or subtract from a starting total**: waterfall.
- **Two-dimensional proportion**: Marimekko (a.k.a. mosaic).

The AI respects a hard limit: it will never emit a pie with more than eight slices. If your data has more than eight categories, the AI will pivot to a treemap or an ordered bar chart. We consider pie-with-thirty-slices a visualization anti-pattern, and we do not enable it even under manual override.

## Distribution Charts

Distribution charts answer *"How is X spread out?"* We ship histogram, density, box plot, violin, strip, scatter, bubble, and 2D hex-bin.

- **Single continuous variable**: histogram (by default) or density (if the AI detects the distribution is smooth and you have at least 200 points).
- **Compare distributions across categories**: grouped box plot or violin.
- **Individual points matter**: strip or swarm.
- **Two continuous variables**: scatter plot.
- **Three dimensions, one a measure**: bubble (size encodes the third).
- **Very large point clouds (>100k points)**: hex-bin or density map.

Every distribution chart supports jitter, outlier detection, and configurable bin sizes. The AI makes sensible defaults; you can override in one follow-up sentence.

## Flow Charts

Flow charts show movement: users between pages, money between accounts, revenue between products. We ship sankey, chord, network graph, and funnel.

- **Directed flows between distinct sets of nodes (e.g., channels → categories → outcomes)**: sankey.
- **Undirected bilateral flows (e.g., country-to-country trade)**: chord.
- **Arbitrary relationships with no fixed layout**: network graph.
- **Sequential drop-off (marketing funnel, checkout funnel)**: funnel or stepped funnel.

Sankeys and chords can get visually messy at high node counts. The AI enforces a soft cap (64 nodes) and asks you to filter or aggregate if you exceed it. You can override, but you will see a warning banner reminding you that readability suffers.

## Geospatial Charts

Phase 1 ships three geospatial chart types: choropleth (fill-by-region), bubble map (size-by-location), and heat map (density). Every map supports the standard base layers (light, dark, satellite) and can be projected in Mercator, Equal Earth, or Natural Earth. Our choropleths understand country codes (ISO 3166), US states, Canadian provinces, and about twenty other common administrative levels out of the box.

Phase 2 expands the geospatial offering significantly with custom TopoJSON uploads, route visualizations, and origin-destination pairs. If you need those capabilities today, contact us — we often run Phase 2 features as private previews for Phase 1 customers.

## KPI Tiles

KPIs are the smallest and most important members of the library. They include gauge, bullet chart, scorecard, delta card, and sparkline. Every KPI tile supports thresholds (green/amber/red), comparison periods (week-over-week, month-over-month, year-over-year), and drill-down to a detail chart with a single click.

The AI reaches for KPIs whenever your question implies a single number. *"What is our monthly recurring revenue right now?"* gets a delta card with MoM change. *"How many active users this week versus last?"* gets a sparkline with the two most recent points highlighted. These tiles are the default entry point for executive dashboards.

## AI-Driven Chart Selection

Everything above assumed the AI picks the chart for you. That is the default, and it is right roughly 90% of the time. When it is wrong, the fix is trivial. Any of the following follow-up sentences will retype the chart in-place:

- *"Make it a sankey."*
- *"Use a heatmap."*
- *"Show it as percent-stacked instead."*
- *"I want a line, not an area chart."*
- *"Put it on a map."*

The agent keeps the underlying data and the query constant; only the render changes. If your new chart type cannot legally represent the data (e.g., you ask for a pie of a time series), the agent will politely decline and suggest the nearest valid alternative.

## Performance

All seventy-two chart types run on a shared canvas-first pipeline with optional SVG fallback for smaller sets. The pipeline supports virtualization: a line chart can render 500,000 points without breaking forty frames per second on a modern laptop, because we only rasterize the visible window.

Bar charts, pies, and trees are cheap and render under 16ms for typical data sizes. Sankeys and network graphs are the most expensive; we cap them at 64 nodes / 512 edges by default and fall back to a DAG-layout worker thread above that. Large scatters use a WebGL renderer behind the scenes.

Dashboards composed of many tiles benefit from **incremental render**: tiles become interactive one by one as their queries return, rather than all at once. Users see first paint in about 400 milliseconds even on dashboards with twenty tiles.

## Accessibility

Every chart ships with keyboard navigation, ARIA roles, and screen-reader-friendly summaries. When a screen reader focuses a chart, it hears a concise summary ("Line chart of weekly active users over the last 52 weeks, ranging from 12,430 to 28,104, peaking in week 47") and can tab through individual data points.

Color ramps default to a color-blind-safe palette (IBM's modified 8-color palette). Organizations can override with their corporate brand, and the platform flags any palette that fails WCAG AA contrast on primary backgrounds.

## Export and Embedding

Every chart can be exported as PNG, SVG, CSV (underlying data), or JSON (full chart spec). Exports respect your current filters, zoom level, and annotations. Embeddings are signed with short-lived tokens so you can drop a chart into Notion, Confluence, or a custom internal portal without leaking raw data.

## What Phase 2 Adds

Phase 2 expands the library with several premium chart types: parallel coordinates, radar multi-series overlays, calendar heatmaps, Gantt, swimlane timelines, and a richer geospatial set. All will plug into the same unified renderer, the same AI selection logic, and the same accessibility layer.

## In Closing

Visualization is where analytics stops being abstract and becomes useful. Our bet on AIInsights365.net is that teams should never have to argue about whether the right chart is a sunburst or a treemap — the AI should pick one, pick it well, and let humans stay focused on the business question. Seventy-two chart types is a lot of surface area; run through this document, try a few of the categories most relevant to your data, and let the AI surprise you in the rest.
