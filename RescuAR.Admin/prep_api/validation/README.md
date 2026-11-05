# PREP research documentation

This directory documents research/reference validation. Its metrics, source maps
and scripts do not establish a live inundation predictor or automatic production
promotion. For deployed endpoint/configuration contracts, start with the
[API guide](../README_IMPLEMENTATION.md).

| Task | Read |
| --- | --- |
| Understand current validation/calibration and incomplete promotion connection | [Workflow](VALIDATION_WORKFLOW.md) |
| Perform independent tracing and peer evaluation | [Peer guide](README_PEER_VALIDATION.md), [trace markers](TRACE_MARKERS.md), [peer outputs](peer_traces/README.md) |
| Interpret primary/supporting thresholds | [Acceptance criteria](ACCEPTANCE_CRITERIA.md) |
| Interpret frozen Ulysses partition, confusion matrices and population proxies | [Spatial partition](SPATIAL_PARTITION_AND_CONFUSION_MATRIX.md) |
| Inspect registered events and metadata fields | [Event registry](events/README.md) |
| Intake MCDRRMO source maps with independent coverage/QC | [MCDRRMO procedure](MCDRRMO_EVENT_INTEGRATION.md), [templates](events/mcdrrmo_intake_templates/README.md) |
| Review Espina/Ecological Profile source provenance | [Event implementation](EVENT_DATA_IMPLEMENTATION.md), [source notes](events/SOURCE_IMPLEMENTATION_NOTES.md), [shared context](events/context/README.md) |
| Locate older superseded procedures | [Legacy index](legacy/README.md); historical only |

Use the [repository catalog](../../../docs/catalog.md) to locate individual event
reference pages. Source images and sample values are evidence to review, not
instructions to synthesize observations or alter ground truth to meet a score.
