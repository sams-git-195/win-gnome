namespace WinGnome.Core.Tests.Connectivity;

/// <summary>
/// Saved-profile XML as <c>WlanGetProfile</c> returns it (tab-indented, the key still encrypted and <c>protected</c>).
/// Every key blob starts with <see cref="KeyMarker"/> so a test can prove it never leaks.
/// </summary>
internal static class WifiProfileSamples
{
    /// <summary>The first bytes of every DPAPI-protected <c>keyMaterial</c> in the samples.</summary>
    public const string KeyMarker = "01000000D08C9DDF0115D1118C7A00C04FC297EB";

    public const string Wpa2Auto = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<name>Home</name>
        	<SSIDConfig>
        		<SSID>
        			<hex>486F6D65</hex>
        			<name>Home</name>
        		</SSID>
        		<nonBroadcast>true</nonBroadcast>
        	</SSIDConfig>
        	<connectionType>ESS</connectionType>
        	<connectionMode>auto</connectionMode>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>WPA2PSK</authentication>
        				<encryption>AES</encryption>
        				<useOneX>false</useOneX>
        			</authEncryption>
        			<sharedKey>
        				<keyType>passPhrase</keyType>
        				<protected>true</protected>
        				<keyMaterial>01000000D08C9DDF0115D1118C7A00C04FC297EB0100000062A1</keyMaterial>
        			</sharedKey>
        		</security>
        	</MSM>
        	<MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
        		<enableRandomization>false</enableRandomization>
        		<randomizationSeed>1273904161</randomizationSeed>
        	</MacRandomization>
        </WLANProfile>
        """;

    public const string Wpa3Manual = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<name>Fibra5G</name>
        	<SSIDConfig>
        		<SSID>
        			<hex>46696272613547</hex>
        			<name>Fibra5G</name>
        		</SSID>
        	</SSIDConfig>
        	<connectionType>ESS</connectionType>
        	<connectionMode>manual</connectionMode>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>WPA3SAE</authentication>
        				<encryption>AES</encryption>
        				<useOneX>false</useOneX>
        				<transitionMode xmlns="http://www.microsoft.com/networking/WLAN/profile/v4">true</transitionMode>
        			</authEncryption>
        			<sharedKey>
        				<keyType>passPhrase</keyType>
        				<protected>true</protected>
        				<keyMaterial>01000000D08C9DDF0115D1118C7A00C04FC297EB01000000A7F3</keyMaterial>
        			</sharedKey>
        		</security>
        	</MSM>
        </WLANProfile>
        """;

    public const string Open = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<name>Cafe Wi-Fi</name>
        	<SSIDConfig>
        		<SSID>
        			<hex>436166652057692D4669</hex>
        			<name>Cafe Wi-Fi</name>
        		</SSID>
        	</SSIDConfig>
        	<connectionType>ESS</connectionType>
        	<connectionMode>auto</connectionMode>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>open</authentication>
        				<encryption>none</encryption>
        				<useOneX>false</useOneX>
        			</authEncryption>
        		</security>
        	</MSM>
        </WLANProfile>
        """;

    public const string Owe = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<name>Guest</name>
        	<SSIDConfig>
        		<SSID>
        			<hex>4775657374</hex>
        			<name>Guest</name>
        		</SSID>
        	</SSIDConfig>
        	<connectionType>ESS</connectionType>
        	<connectionMode>manual</connectionMode>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>OWE</authentication>
        				<encryption>AES</encryption>
        				<useOneX>false</useOneX>
        			</authEncryption>
        		</security>
        	</MSM>
        </WLANProfile>
        """;

    public const string Wep = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<name>OldRouter</name>
        	<SSIDConfig>
        		<SSID>
        			<hex>4F6C64526F75746572</hex>
        			<name>OldRouter</name>
        		</SSID>
        	</SSIDConfig>
        	<connectionType>ESS</connectionType>
        	<connectionMode>auto</connectionMode>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>open</authentication>
        				<encryption>WEP</encryption>
        				<useOneX>false</useOneX>
        			</authEncryption>
        			<sharedKey>
        				<keyType>networkKey</keyType>
        				<protected>true</protected>
        				<keyMaterial>01000000D08C9DDF0115D1118C7A00C04FC297EB01000000C4D2</keyMaterial>
        			</sharedKey>
        			<keyIndex>0</keyIndex>
        		</security>
        	</MSM>
        </WLANProfile>
        """;

    public const string OneX = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<name>Corp-Wifi</name>
        	<SSIDConfig>
        		<SSID>
        			<hex>436F72702D57696669</hex>
        			<name>Corp-Wifi</name>
        		</SSID>
        	</SSIDConfig>
        	<connectionType>ESS</connectionType>
        	<connectionMode>auto</connectionMode>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>WPA2</authentication>
        				<encryption>AES</encryption>
        				<useOneX>true</useOneX>
        			</authEncryption>
        			<OneX xmlns="http://www.microsoft.com/networking/OneX/v1">
        				<authMode>user</authMode>
        				<EAPConfig>
        					<EapHostConfig xmlns="http://www.microsoft.com/provisioning/EapHostConfig">
        						<EapMethod>
        							<Type xmlns="http://www.microsoft.com/provisioning/EapCommon">25</Type>
        							<VendorId xmlns="http://www.microsoft.com/provisioning/EapCommon">0</VendorId>
        						</EapMethod>
        						<Config xmlns="http://www.microsoft.com/provisioning/EapHostConfig">
        							<name>not the profile name</name>
        							<keyMaterial>01000000D08C9DDF0115D1118C7A00C04FC297EB01000000E1B7</keyMaterial>
        						</Config>
        					</EapHostConfig>
        				</EAPConfig>
        			</OneX>
        		</security>
        	</MSM>
        </WLANProfile>
        """;

    /// <summary>A network whose name isn't ASCII (UTF-8 bytes 43 61 66 C3A9 20 E29895); the profile name differs from the SSID's text.</summary>
    public const string NonAscii = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<name>Café ☕ 2</name>
        	<SSIDConfig>
        		<SSID>
        			<hex>436166C3A920E29895</hex>
        			<name>Café ☕</name>
        		</SSID>
        	</SSIDConfig>
        	<connectionType>ESS</connectionType>
        	<connectionMode>auto</connectionMode>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>WPA2PSK</authentication>
        				<encryption>AES</encryption>
        				<useOneX>false</useOneX>
        			</authEncryption>
        			<sharedKey>
        				<keyType>passPhrase</keyType>
        				<protected>true</protected>
        				<keyMaterial>01000000D08C9DDF0115D1118C7A00C04FC297EB01000000D00D</keyMaterial>
        			</sharedKey>
        		</security>
        	</MSM>
        </WLANProfile>
        """;
}
