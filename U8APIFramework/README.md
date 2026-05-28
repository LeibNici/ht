# U8APIFramework

This directory is copied from the official U8API Framework package used for
U8ApiBroker integration.

GitHub Actions copies the entire directory into the release package root. At
runtime the Bridge probes `U8APIFramework` beside `Xinchuan.U8Bridge.exe` before
falling back to `U8_API_DLL_DIR` and standard U8 install paths.

If the customer environment requires a different official U8 version, replace
this directory with the matching `U8APIFramework` folder before packaging.
