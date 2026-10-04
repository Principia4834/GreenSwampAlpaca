using System;
using System.Collections.Generic;

namespace ASCOM.Alpaca
{
    internal class ValidAlpacaKeys
    {
        internal static List<string> ValidParameterKeys = [
            "clientid" ,
            "clienttransactionid",
            "rightascension",
            "declination",
            "id",
            "sensorname",
            "axis"
        ];

        private static List<string> OptionalFormKeys =
        [
            "ClientID",
            "ClientTransactionID"
        ];

        private static List<string> ValidFormKeys = [
            "ClientID",
            "ClientTransactionID",
            "BinX",
            "BinY",
            "CoolerOn",
            "FastReadout",
            "Gain",
            "NumX",
            "NumY",
            "Offset",
            "ReadoutMode",
            "SetCCDTemperature",
            "StartX",
            "StartY",
            "SubExposureDuration",
            "Direction",
            "Duration",
            "Light",
            "Action",
            "Parameters",
            "Command",
            "Raw",
            "Connected",
            "Brightness",
            "Slaved",
            "Altitude",
            "Azimuth",
            "Position",
            "TempComp",
            "AveragePeriod",
            "Reverse",
            "Id",
            "State",
            "Name",
            "Value",
            "Axis",
            "DeclinationRate",
            "RightAscension",
            "Declination",
            "DoesRefraction",
            "GuideRateDeclination",
            "GuideRateRightAscension",
            "Axis",
            "Rate",
            "RightAscensionRate",
            "SideOfPier",
            "SiteElevation",
            "SiteLatitude",
            "SiteLongitude",
            "SlewSettleTime",
            "TargetDeclination",
            "TargetRightAscension",
            "Tracking",
            "TrackingRate",
            "UTCDate",
        ];

        internal static List<AlpacaKeyValidator> AlpacaFormValidators = [];

        static ValidAlpacaKeys()
        {
            foreach (var key in ValidFormKeys)
            {
                AlpacaFormValidators.Add(new AlpacaKeyValidator(key, OptionalFormKeys.Contains(key)));
            }
        }
    }

    internal class AlpacaKeyValidator
    {
        internal bool IsOptional
        {
            get;
            private set;
        }

        internal string Key
        {
            get;
            private set;
        }

        internal bool ExternalKeyFailsValidation(string external_key)
        {
            //Fails capitalization test
            return string.Equals(Key, external_key, StringComparison.OrdinalIgnoreCase) && Key != external_key;
        }

        internal AlpacaKeyValidator(string key, bool is_optional = false)
        {
            Key = key;
            IsOptional = is_optional;
        }
    }
}