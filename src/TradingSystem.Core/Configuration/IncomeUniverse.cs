using TradingSystem.Core.Models;

namespace TradingSystem.Core.Configuration;

/// <summary>
/// Defines the universe of securities eligible for the Income sleeve
/// </summary>
public class IncomeUniverse
{
    public List<IncomeSecurity> Securities { get; set; } = GetDefaultUniverse();
    
    public static List<IncomeSecurity> GetDefaultUniverse()
    {
        return new List<IncomeSecurity>
        {
            // ===== Dividend Growth ETFs (25% target) =====
            new IncomeSecurity
            {
                Symbol = "VIG",
                Name = "Vanguard Dividend Appreciation ETF",
                Category = IncomeCategory.DividendGrowthETF,
                Notes = "Large-cap dividend growers, low cost"
            },
            new IncomeSecurity
            {
                Symbol = "SCHD",
                Name = "Schwab US Dividend Equity ETF",
                Category = IncomeCategory.DividendGrowthETF,
                Notes = "Quality dividend focus, good yield"
            },
            new IncomeSecurity
            {
                Symbol = "DGRO",
                Name = "iShares Core Dividend Growth ETF",
                Category = IncomeCategory.DividendGrowthETF,
                Notes = "Broad dividend growth exposure"
            },
            new IncomeSecurity
            {
                Symbol = "VYM",
                Name = "Vanguard High Dividend Yield ETF",
                Category = IncomeCategory.DividendGrowthETF,
                Notes = "Higher yield, value tilt"
            },
            
            // ===== Covered Call ETFs (20% target) =====
            new IncomeSecurity
            {
                Symbol = "JEPI",
                Name = "JPMorgan Equity Premium Income ETF",
                Category = IncomeCategory.CoveredCallETF,
                Notes = "Actively managed, ELN-based, monthly distributions"
            },
            new IncomeSecurity
            {
                Symbol = "JEPQ",
                Name = "JPMorgan Nasdaq Equity Premium Income ETF",
                Category = IncomeCategory.CoveredCallETF,
                Notes = "NASDAQ-focused version of JEPI"
            },
            new IncomeSecurity
            {
                Symbol = "XYLD",
                Name = "Global X S&P 500 Covered Call ETF",
                Category = IncomeCategory.CoveredCallETF,
                Notes = "S&P 500 covered call strategy"
            },
            new IncomeSecurity
            {
                Symbol = "QYLD",
                Name = "Global X NASDAQ 100 Covered Call ETF",
                Category = IncomeCategory.CoveredCallETF,
                Notes = "NASDAQ covered call, higher yield, more NAV erosion risk"
            },
            
            // ===== BDCs (20% target) =====
            new IncomeSecurity
            {
                Symbol = "ARCC",
                Name = "Ares Capital Corporation",
                Category = IncomeCategory.BDC,
                Notes = "Largest BDC, diversified portfolio, strong track record"
            },
            new IncomeSecurity
            {
                Symbol = "MAIN",
                Name = "Main Street Capital",
                Category = IncomeCategory.BDC,
                Notes = "Internal management, monthly dividends, lower middle market focus"
            },
            new IncomeSecurity
            {
                Symbol = "HTGC",
                Name = "Hercules Capital",
                Category = IncomeCategory.BDC,
                Notes = "Tech/life sciences focus, venture lending"
            },
            new IncomeSecurity
            {
                Symbol = "OBDC",
                Name = "Blue Owl Capital Corporation",
                Category = IncomeCategory.BDC,
                Notes = "Blue Owl/Owl Rock merger, large scale"
            },
            new IncomeSecurity
            {
                Symbol = "GBDC",
                Name = "Golub Capital BDC",
                Category = IncomeCategory.BDC,
                Notes = "Middle market focus, conservative underwriting"
            },
            
            // ===== Equity REITs (10% target) =====
            new IncomeSecurity
            {
                Symbol = "O",
                Name = "Realty Income Corporation",
                Category = IncomeCategory.EquityREIT,
                Notes = "Monthly dividend, triple-net lease, diversified retail"
            },
            new IncomeSecurity
            {
                Symbol = "STAG",
                Name = "STAG Industrial",
                Category = IncomeCategory.EquityREIT,
                Notes = "Industrial/logistics, monthly dividend"
            },
            new IncomeSecurity
            {
                Symbol = "ADC",
                Name = "Agree Realty Corporation",
                Category = IncomeCategory.EquityREIT,
                Notes = "Net lease retail, investment grade tenants"
            },
            new IncomeSecurity
            {
                Symbol = "NNN",
                Name = "NNN REIT",
                Category = IncomeCategory.EquityREIT,
                Notes = "Triple net retail, long dividend growth streak"
            },
            new IncomeSecurity
            {
                Symbol = "VICI",
                Name = "VICI Properties",
                Category = IncomeCategory.EquityREIT,
                Notes = "Gaming/hospitality, experiential real estate"
            },
            
            // ===== Mortgage REITs (10% target) =====
            new IncomeSecurity
            {
                Symbol = "AGNC",
                Name = "AGNC Investment Corp",
                Category = IncomeCategory.MortgageREIT,
                Notes = "Agency MBS focus, monthly dividend, interest rate sensitive"
            },
            new IncomeSecurity
            {
                Symbol = "NLY",
                Name = "Annaly Capital Management",
                Category = IncomeCategory.MortgageREIT,
                Notes = "Largest mREIT, diversified mortgage strategies"
            },
            new IncomeSecurity
            {
                Symbol = "STWD",
                Name = "Starwood Property Trust",
                Category = IncomeCategory.MortgageREIT,
                Notes = "Commercial mortgage focus, diversified"
            },
            new IncomeSecurity
            {
                Symbol = "BXMT",
                Name = "Blackstone Mortgage Trust",
                Category = IncomeCategory.MortgageREIT,
                Notes = "Blackstone-managed, senior commercial loans"
            },
            
            // ===== Preferreds / IG Credit (10% target) =====
            new IncomeSecurity
            {
                Symbol = "PFF",
                Name = "iShares Preferred & Income Securities ETF",
                Category = IncomeCategory.PreferredsIGCredit,
                Notes = "Broad preferred stock exposure"
            },
            new IncomeSecurity
            {
                Symbol = "PGX",
                Name = "Invesco Preferred ETF",
                Category = IncomeCategory.PreferredsIGCredit,
                Notes = "Financial sector preferred focus"
            },
            new IncomeSecurity
            {
                Symbol = "PFFD",
                Name = "Global X U.S. Preferred ETF",
                Category = IncomeCategory.PreferredsIGCredit,
                Notes = "Diversified preferreds, lower cost"
            },
            new IncomeSecurity
            {
                Symbol = "LQD",
                Name = "iShares iBoxx Investment Grade Corporate Bond ETF",
                Category = IncomeCategory.PreferredsIGCredit,
                Notes = "Investment grade corporate bonds, ballast"
            },
            new IncomeSecurity
            {
                Symbol = "VCIT",
                Name = "Vanguard Intermediate-Term Corporate Bond ETF",
                Category = IncomeCategory.PreferredsIGCredit,
                Notes = "Intermediate duration IG corporates"
            }
        };
    }
    
    public List<IncomeSecurity> GetByCategory(IncomeCategory category)
    {
        return Securities.Where(s => s.Category == category).ToList();
    }
    
    public IncomeSecurity? GetBySymbol(string symbol)
    {
        return Securities.FirstOrDefault(s =>
            s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryGetCategory(string symbol, out IncomeCategory category)
    {
        var security = GetBySymbol(symbol);
        if (security != null)
        {
            category = security.Category;
            return true;
        }
        category = default;
        return false;
    }
}

public class IncomeSecurity
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IncomeCategory Category { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    
    // Quality data (populated at runtime)
    public IncomeSecurityQuality? Quality { get; set; }
}
