using ChatPortal2.Models;

namespace ChatPortal2.Services;

public class ChartService : IChartService
{
    private readonly List<ChartTypeInfo> _charts;

    public ChartService()
    {
        _charts = new List<ChartTypeInfo>
        {
            // Basic (10)
            new() { Id="bar", Name="Bar Chart", Group="Basic", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Compare values across categories" },
            new() { Id="horizontalBar", Name="Horizontal Bar", Group="Basic", Icon="bi-bar-chart-steps", ChartJsType="bar", Description="Horizontal bar comparison" },
            new() { Id="stackedBar", Name="Stacked Bar", Group="Basic", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Show composition and totals" },
            new() { Id="line", Name="Line Chart", Group="Basic", Icon="bi-graph-up", ChartJsType="line", Description="Show trends over time" },
            new() { Id="area", Name="Area Chart", Group="Basic", Icon="bi-graph-up-arrow", ChartJsType="line", Description="Filled area under line" },
            new() { Id="pie", Name="Pie Chart", Group="Basic", Icon="bi-pie-chart-fill", ChartJsType="pie", Description="Show proportions of a whole" },
            new() { Id="donut", Name="Donut Chart", Group="Basic", Icon="bi-pie-chart", ChartJsType="doughnut", Description="Pie with center space" },
            new() { Id="scatter", Name="Scatter Plot", Group="Basic", Icon="bi-diagram-3", ChartJsType="scatter", Description="Show correlation between variables" },
            new() { Id="bubble", Name="Bubble Chart", Group="Basic", Icon="bi-circle", ChartJsType="bubble", Description="3-dimensional scatter plot" },
            new() { Id="radar", Name="Radar Chart", Group="Basic", Icon="bi-broadcast", ChartJsType="radar", Description="Compare multiple attributes" },

            // Advanced (10)
            new() { Id="mixedBarLine", Name="Mixed Bar+Line", Group="Advanced", Icon="bi-bar-chart-line-fill", ChartJsType="bar", Description="Combine bar and line series" },
            new() { Id="groupedBar", Name="Grouped Bar", Group="Advanced", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Side-by-side category comparison" },
            new() { Id="waterfall", Name="Waterfall", Group="Advanced", Icon="bi-bar-chart-steps", ChartJsType="bar", Description="Show cumulative effect of values" },
            new() { Id="funnel", Name="Funnel Chart", Group="Advanced", Icon="bi-funnel-fill", ChartJsType="bar", Description="Show stages in a process" },
            new() { Id="gauge", Name="Gauge Chart", Group="Advanced", Icon="bi-speedometer2", ChartJsType="doughnut", Description="Show progress toward a goal" },
            new() { Id="treemap", Name="Treemap", Group="Advanced", Icon="bi-grid-fill", ChartJsType="bar", Description="Show hierarchical data as rectangles" },
            new() { Id="heatmap", Name="Heatmap", Group="Advanced", Icon="bi-grid-3x3-gap-fill", ChartJsType="bar", Description="Show data density with color" },
            new() { Id="sankey", Name="Sankey Diagram", Group="Advanced", Icon="bi-signpost-split-fill", ChartJsType="bar", Description="Show flow between nodes" },
            new() { Id="sunburst", Name="Sunburst", Group="Advanced", Icon="bi-circle", ChartJsType="pie", Description="Hierarchical pie chart" },
            new() { Id="polarArea", Name="Polar Area", Group="Advanced", Icon="bi-pie-chart-fill", ChartJsType="polarArea", Description="Radial bar chart" },

            // Statistical (10)
            new() { Id="histogram", Name="Histogram", Group="Statistical", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Show frequency distribution" },
            new() { Id="boxPlot", Name="Box Plot", Group="Statistical", Icon="bi-bar-chart-steps", ChartJsType="bar", Description="Show statistical distribution" },
            new() { Id="violin", Name="Violin Plot", Group="Statistical", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Distribution shape visualization" },
            new() { Id="bellCurve", Name="Bell Curve", Group="Statistical", Icon="bi-graph-up", ChartJsType="line", Description="Normal distribution curve" },
            new() { Id="pareto", Name="Pareto Chart", Group="Statistical", Icon="bi-bar-chart-line-fill", ChartJsType="bar", Description="80/20 rule visualization" },
            new() { Id="controlChart", Name="Control Chart", Group="Statistical", Icon="bi-graph-up", ChartJsType="line", Description="Monitor process variation" },
            new() { Id="regressionLine", Name="Regression Line", Group="Statistical", Icon="bi-graph-up-arrow", ChartJsType="scatter", Description="Show linear trend" },
            new() { Id="confidenceBand", Name="Confidence Band", Group="Statistical", Icon="bi-graph-up", ChartJsType="line", Description="Show confidence intervals" },
            new() { Id="errorBar", Name="Error Bar", Group="Statistical", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Show error ranges" },
            new() { Id="stemLeaf", Name="Stem and Leaf", Group="Statistical", Icon="bi-list-columns", ChartJsType="bar", Description="Display individual data points" },

            // Time Series (10)
            new() { Id="timeLine", Name="Time Line", Group="Time Series", Icon="bi-clock-history", ChartJsType="line", Description="Data plotted over time" },
            new() { Id="candlestick", Name="Candlestick", Group="Time Series", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="OHLC financial data" },
            new() { Id="ohlc", Name="OHLC Chart", Group="Time Series", Icon="bi-bar-chart-line", ChartJsType="bar", Description="Open-High-Low-Close" },
            new() { Id="rangeArea", Name="Range Area", Group="Time Series", Icon="bi-graph-up-arrow", ChartJsType="line", Description="Show min-max range over time" },
            new() { Id="stepLine", Name="Step Line", Group="Time Series", Icon="bi-graph-up", ChartJsType="line", Description="Stepped progression chart" },
            new() { Id="streamGraph", Name="Stream Graph", Group="Time Series", Icon="bi-water", ChartJsType="line", Description="Flowing area chart" },
            new() { Id="eventTimeline", Name="Event Timeline", Group="Time Series", Icon="bi-calendar-event", ChartJsType="bar", Description="Show events on a timeline" },
            new() { Id="gantt", Name="Gantt Chart", Group="Time Series", Icon="bi-bar-chart-steps", ChartJsType="bar", Description="Project schedule visualization" },
            new() { Id="burnDown", Name="Burn Down", Group="Time Series", Icon="bi-graph-down", ChartJsType="line", Description="Track remaining work" },
            new() { Id="velocityChart", Name="Velocity Chart", Group="Time Series", Icon="bi-speedometer", ChartJsType="bar", Description="Show sprint velocity" },

            // Comparison (10)
            new() { Id="bulletChart", Name="Bullet Chart", Group="Comparison", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Performance vs target" },
            new() { Id="marimekko", Name="Marimekko", Group="Comparison", Icon="bi-grid-fill", ChartJsType="bar", Description="Variable width bar chart" },
            new() { Id="dotPlot", Name="Dot Plot", Group="Comparison", Icon="bi-dot", ChartJsType="scatter", Description="Simple dot comparison" },
            new() { Id="lollipop", Name="Lollipop Chart", Group="Comparison", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Minimalist bar chart variant" },
            new() { Id="dumbbell", Name="Dumbbell Chart", Group="Comparison", Icon="bi-bar-chart-steps", ChartJsType="bar", Description="Show difference between two points" },
            new() { Id="slope", Name="Slope Chart", Group="Comparison", Icon="bi-graph-up", ChartJsType="line", Description="Compare two time periods" },
            new() { Id="divergingBar", Name="Diverging Bar", Group="Comparison", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Show positive/negative values" },
            new() { Id="spanChart", Name="Span Chart", Group="Comparison", Icon="bi-bar-chart-steps", ChartJsType="bar", Description="Show range with min/max" },
            new() { Id="pairedBar", Name="Paired Bar", Group="Comparison", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Compare two groups side by side" },
            new() { Id="populationPyramid", Name="Population Pyramid", Group="Comparison", Icon="bi-bar-chart-steps", ChartJsType="bar", Description="Age/gender distribution" },

            // Geographic (5)
            new() { Id="choropleth", Name="Choropleth Map", Group="Geographic", Icon="bi-map-fill", ChartJsType="bar", Description="Color-coded geographic areas" },
            new() { Id="bubbleMap", Name="Bubble Map", Group="Geographic", Icon="bi-geo-fill", ChartJsType="bubble", Description="Sized bubbles on map" },
            new() { Id="heatMapGeo", Name="Heat Map Geographic", Group="Geographic", Icon="bi-map", ChartJsType="bar", Description="Density heat map on geography" },
            new() { Id="flowMap", Name="Flow Map", Group="Geographic", Icon="bi-arrows-move", ChartJsType="bar", Description="Show movement between locations" },
            new() { Id="spikeMap", Name="Spike Map", Group="Geographic", Icon="bi-geo-alt-fill", ChartJsType="bar", Description="Spikes proportional to values" },

            // Relationship (5)
            new() { Id="networkGraph", Name="Network Graph", Group="Relationship", Icon="bi-diagram-3-fill", ChartJsType="scatter", Description="Show network connections" },
            new() { Id="chordDiagram", Name="Chord Diagram", Group="Relationship", Icon="bi-circle", ChartJsType="pie", Description="Show relationships between groups" },
            new() { Id="arcDiagram", Name="Arc Diagram", Group="Relationship", Icon="bi-broadcast", ChartJsType="scatter", Description="Connections as arcs" },
            new() { Id="forceDirected", Name="Force-Directed", Group="Relationship", Icon="bi-diagram-2-fill", ChartJsType="scatter", Description="Physics-based network layout" },
            new() { Id="matrix", Name="Matrix Chart", Group="Relationship", Icon="bi-grid-3x3", ChartJsType="bar", Description="Show relationships as a matrix" },

            // Part-to-Whole (5)
            new() { Id="stackedBar100", Name="100% Stacked Bar", Group="Part-to-Whole", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Show 100% composition" },
            new() { Id="stackedArea100", Name="100% Stacked Area", Group="Part-to-Whole", Icon="bi-graph-up-arrow", ChartJsType="line", Description="100% filled area chart" },
            new() { Id="waffleChart", Name="Waffle Chart", Group="Part-to-Whole", Icon="bi-grid-fill", ChartJsType="bar", Description="Grid-based proportion display" },
            new() { Id="pictograph", Name="Pictograph", Group="Part-to-Whole", Icon="bi-person-fill", ChartJsType="bar", Description="Icon-based data visualization" },
            new() { Id="nightingaleRose", Name="Nightingale Rose", Group="Part-to-Whole", Icon="bi-pie-chart-fill", ChartJsType="polarArea", Description="Radial bar chart variant" },

            // KPI/Metrics (5)
            new() { Id="kpiCard", Name="KPI Card", Group="KPI/Metrics", Icon="bi-card-text", ChartJsType="bar", Description="Single metric display card" },
            new() { Id="sparkline", Name="Sparkline", Group="KPI/Metrics", Icon="bi-graph-up", ChartJsType="line", Description="Compact trend indicator" },
            new() { Id="progressBar", Name="Progress Bar", Group="KPI/Metrics", Icon="bi-bar-chart-fill", ChartJsType="bar", Description="Goal completion indicator" },
            new() { Id="radialProgress", Name="Radial Progress", Group="KPI/Metrics", Icon="bi-pie-chart", ChartJsType="doughnut", Description="Circular progress indicator" },
            new() { Id="metricTile", Name="Metric Tile", Group="KPI/Metrics", Icon="bi-grid-1x2-fill", ChartJsType="bar", Description="Dashboard metric tile" },

            // Data / Interactive (2)
            new() { Id="table", Name="Table Chart", Group="Data / Interactive", Icon="bi-table", ChartJsType="bar", Description="Display data in a tabular format" },
            new() { Id="slicer", Name="Slicer", Group="Data / Interactive", Icon="bi-funnel", ChartJsType="bar", Description="Interactive data filter control" },
        };
    }

    public List<ChartTypeInfo> GetChartLibrary() => _charts;

    public IEnumerable<IGrouping<string, ChartTypeInfo>> GetGroupedCharts() =>
        _charts.GroupBy(c => c.Group);

    public List<ChartDefinition> GetDefaultCharts() => new();
}
