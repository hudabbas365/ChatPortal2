namespace AIInsights.Models;

public static class SampleDataSets
{
    public static string Sales => @"[
  {""month"":""Jan"",""region"":""North"",""product"":""Widget A"",""revenue"":42000,""units"":320,""profit"":12000,""growth"":5.2},
  {""month"":""Jan"",""region"":""South"",""product"":""Widget B"",""revenue"":35000,""units"":280,""profit"":9800,""growth"":3.1},
  {""month"":""Jan"",""region"":""East"",""product"":""Widget C"",""revenue"":28000,""units"":190,""profit"":7500,""growth"":7.4},
  {""month"":""Feb"",""region"":""North"",""product"":""Widget A"",""revenue"":45000,""units"":350,""profit"":13500,""growth"":7.1},
  {""month"":""Feb"",""region"":""South"",""product"":""Widget B"",""revenue"":38000,""units"":295,""profit"":10200,""growth"":8.6},
  {""month"":""Feb"",""region"":""East"",""product"":""Widget C"",""revenue"":31000,""units"":210,""profit"":8200,""growth"":10.7},
  {""month"":""Mar"",""region"":""North"",""product"":""Widget A"",""revenue"":51000,""units"":400,""profit"":15000,""growth"":13.3},
  {""month"":""Mar"",""region"":""South"",""product"":""Widget B"",""revenue"":41000,""units"":320,""profit"":11500,""growth"":7.9},
  {""month"":""Mar"",""region"":""East"",""product"":""Widget C"",""revenue"":34000,""units"":230,""profit"":9100,""growth"":9.7},
  {""month"":""Apr"",""region"":""North"",""product"":""Widget A"",""revenue"":48000,""units"":380,""profit"":14200,""growth"":6.1},
  {""month"":""Apr"",""region"":""South"",""product"":""Widget B"",""revenue"":43000,""units"":340,""profit"":12000,""growth"":4.9},
  {""month"":""Apr"",""region"":""East"",""product"":""Widget C"",""revenue"":36000,""units"":245,""profit"":9800,""growth"":5.9}
]";

    public static string Population => @"[
  {""country"":""China"",""continent"":""Asia"",""population"":1411750000,""gdp"":17734000,""lifeExpectancy"":77.1,""area"":9596960},
  {""country"":""India"",""continent"":""Asia"",""population"":1392329000,""gdp"":3176300,""lifeExpectancy"":69.7,""area"":3287263},
  {""country"":""USA"",""continent"":""Americas"",""population"":331893745,""gdp"":23315081,""lifeExpectancy"":78.9,""area"":9833517},
  {""country"":""Indonesia"",""continent"":""Asia"",""population"":273753191,""gdp"":1186093,""lifeExpectancy"":71.7,""area"":1904569},
  {""country"":""Pakistan"",""continent"":""Asia"",""population"":220892331,""gdp"":348258,""lifeExpectancy"":67.3,""area"":881913},
  {""country"":""Brazil"",""continent"":""Americas"",""population"":214326223,""gdp"":1608981,""lifeExpectancy"":75.9,""area"":8515767},
  {""country"":""Nigeria"",""continent"":""Africa"",""population"":211401000,""gdp"":440831,""lifeExpectancy"":54.7,""area"":923768},
  {""country"":""Bangladesh"",""continent"":""Asia"",""population"":167885989,""gdp"":355000,""lifeExpectancy"":72.6,""area"":147570},
  {""country"":""Russia"",""continent"":""Europe"",""population"":145102755,""gdp"":1829050,""lifeExpectancy"":72.6,""area"":17098242},
  {""country"":""Mexico"",""continent"":""Americas"",""population"":126705138,""gdp"":1272839,""lifeExpectancy"":75.1,""area"":1964375},
  {""country"":""Ethiopia"",""continent"":""Africa"",""population"":120812698,""gdp"":111271,""lifeExpectancy"":66.6,""area"":1104300},
  {""country"":""Japan"",""continent"":""Asia"",""population"":125681593,""gdp"":4937422,""lifeExpectancy"":84.3,""area"":377975}
]";

    public static string Weather => @"[
  {""date"":""2024-01-01"",""city"":""New York"",""tempHigh"":2,""tempLow"":-5,""precipitation"":12,""humidity"":68,""windSpeed"":18},
  {""date"":""2024-02-01"",""city"":""New York"",""tempHigh"":4,""tempLow"":-3,""precipitation"":8,""humidity"":65,""windSpeed"":16},
  {""date"":""2024-03-01"",""city"":""New York"",""tempHigh"":11,""tempLow"":3,""precipitation"":10,""humidity"":62,""windSpeed"":14},
  {""date"":""2024-04-01"",""city"":""New York"",""tempHigh"":17,""tempLow"":8,""precipitation"":14,""humidity"":60,""windSpeed"":13},
  {""date"":""2024-05-01"",""city"":""New York"",""tempHigh"":23,""tempLow"":14,""precipitation"":11,""humidity"":62,""windSpeed"":12},
  {""date"":""2024-06-01"",""city"":""New York"",""tempHigh"":29,""tempLow"":20,""precipitation"":9,""humidity"":65,""windSpeed"":11},
  {""date"":""2024-07-01"",""city"":""New York"",""tempHigh"":32,""tempLow"":23,""precipitation"":13,""humidity"":68,""windSpeed"":10},
  {""date"":""2024-08-01"",""city"":""New York"",""tempHigh"":31,""tempLow"":22,""precipitation"":11,""humidity"":66,""windSpeed"":10},
  {""date"":""2024-09-01"",""city"":""New York"",""tempHigh"":26,""tempLow"":17,""precipitation"":8,""humidity"":63,""windSpeed"":12},
  {""date"":""2024-10-01"",""city"":""New York"",""tempHigh"":18,""tempLow"":10,""precipitation"":10,""humidity"":65,""windSpeed"":14},
  {""date"":""2024-11-01"",""city"":""New York"",""tempHigh"":11,""tempLow"":4,""precipitation"":11,""humidity"":67,""windSpeed"":15},
  {""date"":""2024-12-01"",""city"":""New York"",""tempHigh"":4,""tempLow"":-2,""precipitation"":10,""humidity"":70,""windSpeed"":17}
]";

    public static string Finance => @"[
  {""ticker"":""AAPL"",""sector"":""Technology"",""open"":182.5,""high"":185.3,""low"":181.1,""close"":184.2,""volume"":58234000,""marketCap"":2850000,""peRatio"":28.5},
  {""ticker"":""MSFT"",""sector"":""Technology"",""open"":375.2,""high"":378.9,""low"":374.1,""close"":377.5,""volume"":22145000,""marketCap"":2800000,""peRatio"":34.2},
  {""ticker"":""GOOGL"",""sector"":""Technology"",""open"":140.3,""high"":142.8,""low"":139.5,""close"":141.9,""volume"":24356000,""marketCap"":1790000,""peRatio"":26.1},
  {""ticker"":""AMZN"",""sector"":""Consumer"",""open"":178.6,""high"":181.2,""low"":177.9,""close"":180.1,""volume"":31245000,""marketCap"":1870000,""peRatio"":62.3},
  {""ticker"":""NVDA"",""sector"":""Technology"",""open"":495.2,""high"":502.5,""low"":493.1,""close"":499.8,""volume"":45678000,""marketCap"":1230000,""peRatio"":64.5},
  {""ticker"":""META"",""sector"":""Technology"",""open"":350.1,""high"":354.7,""low"":348.9,""close"":352.4,""volume"":19876000,""marketCap"":905000,""peRatio"":23.1},
  {""ticker"":""TSLA"",""sector"":""Automotive"",""open"":242.5,""high"":247.8,""low"":241.2,""close"":245.3,""volume"":87654000,""marketCap"":779000,""peRatio"":71.4},
  {""ticker"":""BRK"",""sector"":""Finance"",""open"":356.2,""high"":358.9,""low"":355.1,""close"":357.8,""volume"":4321000,""marketCap"":782000,""peRatio"":8.9},
  {""ticker"":""JPM"",""sector"":""Finance"",""open"":196.3,""high"":198.7,""low"":195.8,""close"":197.5,""volume"":12345000,""marketCap"":572000,""peRatio"":12.3},
  {""ticker"":""JNJ"",""sector"":""Healthcare"",""open"":158.1,""high"":159.8,""low"":157.5,""close"":158.9,""volume"":8765000,""marketCap"":417000,""peRatio"":15.7}
]";
}
