# UltraWide provenance inside Lunar127 1.1.0

The resident 1,048,576-bit macroprogram word was generated with the attached UltraWide Appliance 0.2.0 preview's `LCTL-BRIM/1` compiler and `BRWWORD/1` packer/verifier.

Lunar127 changes the execution role: the UltraWide word is verified once at boot and then retained as the cryptographic macroprogram anchor for TQK-1/TQL-1/TQV-1. Lunar127 does not run Python BRIM or million-bit arithmetic inside the 1 MHz hot loop.

The original UltraWide preview uses the Product Preview Tester License 1.1. A copy is carried beside this notice in the hologram. This Lunar127 integration is an evaluation/research modification and is not represented as an unchanged upstream UltraWide release.
