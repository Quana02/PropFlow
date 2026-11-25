# Recognition quality fixtures

`complex-source.jpg` is the user-supplied Test.jpg (permission to use and send to
the configured provider was given earlier in this conversation). The reference
file marks only the two visible water regions, in original-image pixels. It is
**not** a complete annotation of parking bays, roads, trees or gates. Tests must
not report full diagram accuracy from these two landmarks.

The test dataset also includes the real B2/B3 snapshots in `../parking-b2` and
generated 400-bay ground truths at 0, 17, 45 and 135 degrees. The 17-degree case
checks an angle different from the original diagonal example. Generated images
include inner vehicle symbols and independently known outer boundaries.

Run `ParkingGeometryDatasetTests` with `PROPFLOW_PARKING_EVALUATION_DIR` pointing
to a local output directory to export timings, missing tool counts, overlap
counts and normalized corner differences. These are geometry/replay tests,
**not live Gemini recognition accuracy**. Provider snapshots are not a substitute
for independently annotating more unseen real drawings.
