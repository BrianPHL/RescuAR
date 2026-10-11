# Legacy historical probe retained from the uploaded project

`ulysses_sto_nino.json` is preserved because it was already present in the uploaded `RescuAR.Admin(4).zip`.

It is **not usable training data**:

- 144 timestamped rows are present for approximately 24 hours around 12 November 2020;
- every `water_level_m` value is `null`;
- every `raw_water_level` value is `-`.

This indicates that the tested current Sto. Niño MMDA station code can return historical timestamps without actual archived water-level values for that 2020 query.

The new collector therefore rejects these rows rather than treating `-` as zero or fabricating the documented Ulysses peak. Test the alternate historical station codes with `npm run test:archive` before relying on PAGASA's endpoint for training data. If the archive remains empty, obtain the historical sensor series through PAGASA/MMDA eFOI or another documented source.
