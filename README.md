# PREREQUISITES
- [Git](https://git-scm.com/install/windows)

# INSTALL
- Download the .7z file under the latest release
- Extract the .7z file to a folder that you are okay with using to store your unity projects
- Install the [Unity Hub](https://docs.unity.com/en-us/hub/install-hub-win-mac)
- Once you have the Unity Hub installed, ensure you are in the projects tab on the left side of the Unity Hub and click the "Add" button in the top right of the window and add project from disk
- Navigate to where you extracted the .7z file and select the folder you extracted from the .7z file, this is your unity project
- Follow the prompts to install Unity version 2022.3.43f1 and related prerequisite software, such as VSCode
- Click on the unity project that is now in your unity hub and wait for it to open

# USING THE SDK

This SDK is intended for the production of assets for Escape From Tarkov running on SPT version 4.0 or newer.
This SDK uses and is dependent on Tarkin's bundle imposter tool, found [here](https://github.com/bmpq/AssetBundles-Browser-Imposter). It is an amazing tool for modding in general, not just tarkov.

### SDK Theory of Operation
The general way the SDK works is by allowing you to build items using references to scripts that are used in Escape From Tarkov and references to assets that are included in the base game without needing to actually include them in your built assets, saving space. The asset replacement is done with the bundle imposter tool which replaces the physical asset with a reference to the asset path ID and the CAB ID of the bundle it is stored in. Think of this like a house number and street address.

The SDK automatically does this when you build your bundles in the Asset Bundle Browser menu. If you have new assets from the base game that you would like to reference, all you need to do is check the "Is Imposter" box in the properties of the asset and fill out the path ID and CAB ID information, found in the bundle you ripped the assets from. Then you must have the assets build to a bundle to represent the bundle they came from in the base game. You will get errors if you have assets with different CAB-IDs assigned to their imposter imformation building to the same bundle. You must separately package each CAB-ID.

### [SDK GENERAL USAGE AND MORE IS COVERED IN THIS GOOGLE DOCUMENT](https://docs.google.com/document/d/1BwaceD-tBaoYuA7reF_Dj-FChIJeC6L4Gy7oSpkMCio/edit?usp=sharing)
