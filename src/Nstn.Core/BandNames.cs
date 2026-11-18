// How the bands are called on screen, keyed as in the night model.

namespace Nstn.Core {

    public static class BandNames {

        public const string Broadband = "RGB";

        /// <summary>"H-alpha", "O III", "L/RGB" - for summaries and row labels.</summary>
        public static string Short(string key) => key switch {
            Broadband => Text.T("Band_RGB"),
            "Ha" => Text.T("Band_Ha"),
            "OIII" => Text.T("Band_OIII"),
            "SII" => Text.T("Band_SII"),
            "Hb" => Text.T("Band_Hb"),
            _ => key,
        };

        /// <summary>"H-alpha 656 nm", "L / RGB (broadband)" - for the band selector.</summary>
        public static string Long(string key) => key switch {
            Broadband => Text.T("Band_RGB_Long"),
            "Ha" => Text.T("Band_Ha_Long"),
            "OIII" => Text.T("Band_OIII_Long"),
            "SII" => Text.T("Band_SII_Long"),
            "Hb" => Text.T("Band_Hb_Long"),
            _ => key,
        };

        /// <summary>"O III 6 nm", or the broadband name alone.</summary>
        public static string WithBandwidth(string key, double bandwidthNm) =>
            key == Broadband
                ? Short(key)
                : string.Format(Text.Culture, "{0} {1:0.#} nm", Short(key), bandwidthNm);
    }
}
