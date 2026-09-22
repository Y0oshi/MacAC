namespace MacAC.Client.Shell.Panels;

internal static class AllegianceRankTitleChart
{
    public const uint AllegianceGradePropIdent = 0x1Eu;

    public static string ConstructWholeLabel(int grade, int lineageCluster, int gender, string label)
    {
        string? banner = FetchBanner(grade, lineageCluster, gender);
        return string.IsNullOrEmpty(banner) ? label : $"{banner} {label}";
    }

    public static string? FetchBanner(int grade, int lineageCluster, int gender)
    {
        if (gender is 1)
        {
            return !IsLineageInSpan(lineageCluster)
                ? null
                : lineageCluster switch
                {
                    1 => FetchAluvianMaleBanner(grade),
                    2 => FetchGharundimMaleBanner(grade),
                    3 => FetchShoMaleBanner(grade),
                    4 => FetchViamontianMaleBanner(grade),
                    5 or 0xA => FetchShadowboundMaleBanner(grade), // 0xA = Penumbraen alias
                    6 => FetchGearknightMaleBanner(grade),
                    7 => FetchTumerokMaleBanner(grade),
                    8 => FetchLugianFemaleBanner(grade), // Lugian authors FEMALE only; reused here
                    9 => FetchEmpyreanMaleBanner(grade),
                    0xB => FetchUndeadMaleBanner(grade),
                    _ => null,
                };
        }

        if (gender is 2)
        {
            return !IsLineageInSpan(lineageCluster)
                ? null
                : lineageCluster switch
                {
                    1 => FetchAluvianFemaleBanner(grade),
                    2 => FetchGharundimFemaleBanner(grade),
                    3 => FetchShoFemaleBanner(grade),
                    4 => FetchViamontianFemaleBanner(grade),
                    5 or 0xA => FetchShadowboundFemaleBanner(grade), // 0xA = Penumbraen alias
                    6 => FetchGearknightMaleBanner(grade), // Gearknight authors MALE only; reused here
                    7 => FetchTumerokMaleBanner(grade), // Tumerok authors MALE only; reused here
                    8 => FetchLugianFemaleBanner(grade),
                    9 => FetchEmpyreanFemaleBanner(grade),
                    0xB => FetchUndeadFemaleBanner(grade),
                    _ => null,
                };
        }

        return null;
    }

    private static bool IsLineageInSpan(int lineageCluster)
        => unchecked((uint)(lineageCluster - 1)) <= 0xAu;

    private static string? FetchAluvianMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Yeoman",
            2 => "Baronet",
            3 => "Baron",
            4 => "Reeve",
            5 => "Thane",
            6 => "Ealdor",
            7 => "Duke",
            8 => "Aetheling",
            9 => "King",
            10 => "High King",
            _ => null,
        };
    }

    private static string? FetchAluvianFemaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Yeoman",
            2 => "Baronet",
            3 => "Baroness",
            4 => "Reeve",
            5 => "Thane",
            6 => "Ealdor",
            7 => "Duchess",
            8 => "Aetheling",
            9 => "Queen",
            10 => "High Queen",
            _ => null,
        };
    }

    private static string? FetchGharundimMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Sayyid",
            2 => "Shayk",
            3 => "Maulan",
            4 => "Mu'allim",
            5 => "Naquib",
            6 => "Qadi",
            7 => "Mushir",
            8 => "Amir",
            9 => "Malik",
            10 => "Sultan",
            _ => null,
        };
    }

    private static string? FetchGharundimFemaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Sayyida",
            2 => "Shayka",
            3 => "Maulana",
            4 => "Mu'allima",
            5 => "Naquiba",
            6 => "Qadiya",
            7 => "Mushira",
            8 => "Amira",
            9 => "Malika",
            10 => "Sultana",
            _ => null,
        };
    }

    private static string? FetchShoMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Jinin",
            2 => "Jo-chueh",
            3 => "Nan-chueh",
            4 => "Shi-chueh",
            5 => "Ta-chueh",
            6 => "Kun-chueh",
            7 => "Kou",
            8 => "Taikou",
            9 => "Ou",
            10 => "Koutei",
            _ => null,
        };
    }

    private static string? FetchShoFemaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Jinin",
            2 => "Jo-chueh",
            3 => "Nan-chueh",
            4 => "Shi-chueh",
            5 => "Ta-chueh",
            6 => "Kun-chueh",
            7 => "Kou",
            8 => "Taikou",
            9 => "Jo-ou",
            10 => "Koutei",
            _ => null,
        };
    }

    private static string? FetchViamontianMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Squire",
            2 => "Banner",
            3 => "Baron",
            4 => "Viscount",
            5 => "Count",
            6 => "Marquis",
            7 => "Duke",
            8 => "Grand Duke",
            9 => "King",
            10 => "High King",
            _ => null,
        };
    }

    private static string? FetchViamontianFemaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Dame",
            2 => "Banner",
            3 => "Baroness",
            4 => "Viscountess",
            5 => "Countess",
            6 => "Marquise",
            7 => "Duchess",
            8 => "Grand Duchess",
            9 => "Queen",
            10 => "High Queen",
            _ => null,
        };
    }

    private static string? FetchShadowboundMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Tenebrous",
            2 => "Shade",
            3 => "Squire",
            4 => "Knight",
            5 => "Void Knight",
            6 => "Void Lord",
            7 => "Duke",
            8 => "Archduke",
            9 => "Highborn",
            10 => "King",
            _ => null,
        };
    }

    private static string? FetchShadowboundFemaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Tenebrous",
            2 => "Shade",
            3 => "Squire",
            4 => "Knight",
            5 => "Void Knight",
            6 => "Void Lady",
            7 => "Duchess",
            8 => "Archduchess",
            9 => "Highborn",
            10 => "Queen",
            _ => null,
        };
    }

    private static string? FetchGearknightMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Tribunus",
            2 => "Praefectus",
            3 => "Optio",
            4 => "Centurion",
            5 => "Principes",
            6 => "Legatus",
            7 => "Consul",
            8 => "Dux",
            9 => "Secondus",
            10 => "Primus",
            _ => null,
        };
    }

    private static string? FetchTumerokMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Xutua",
            2 => "Tuona",
            3 => "Ona",
            4 => "Nuona",
            5 => "Turea",
            6 => "Rea",
            7 => "Nurea",
            8 => "Kauh",
            9 => "Sutah",
            10 => "Tah",
            _ => null,
        };
    }

    private static string? FetchLugianFemaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Laigus",
            2 => "Raigus",
            3 => "Amploth",
            4 => "Arintoth",
            5 => "Obeloth",
            6 => "Lithos",
            7 => "Kantos",
            8 => "Gigas",
            9 => "Extas",
            10 => "Tiatus",
            _ => null,
        };
    }

    private static string? FetchEmpyreanMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Ensign",
            2 => "Corporal",
            3 => "Lieutenant",
            4 => "Commander",
            5 => "Captain",
            6 => "Commodore",
            7 => "Admiral",
            8 => "Warlord",
            9 => "Ipharsin",
            10 => "Aulin",
            _ => null,
        };
    }

    private static string? FetchEmpyreanFemaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Ensign",
            2 => "Corporal",
            3 => "Lieutenant",
            4 => "Commander",
            5 => "Captain",
            6 => "Commodore",
            7 => "Admiral",
            8 => "Warlord",
            9 => "Ipharsia",
            10 => "Aulia",
            _ => null,
        };
    }

    private static string? FetchUndeadMaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Neophyte",
            2 => "Acolyte",
            3 => "Adept",
            4 => "Esquire",
            5 => "Squire",
            6 => "Knight",
            7 => "Count",
            8 => "Viscount",
            9 => "Highness",
            10 => "Annointed",
            _ => null,
        };
    }

    private static string? FetchUndeadFemaleBanner(int grade)
    {
        return grade switch
        {
            1 => "Neophyte",
            2 => "Acolyte",
            3 => "Adept",
            4 => "Esquire",
            5 => "Squire",
            6 => "Knight",
            7 => "Countess",
            8 => "Viscountess",
            9 => "Highness",
            10 => "Annointed",
            _ => null,
        };
    }
}
