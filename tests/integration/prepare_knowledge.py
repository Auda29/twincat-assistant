"""Synthetic upstream package fixture; never Beckhoff documentation."""
import importlib.util
import json
import sys
from pathlib import Path

checkout, root = map(Path, sys.argv[1:])
helper = checkout / "tests/contract/topic_mcp_launcher.py"
spec = importlib.util.spec_from_file_location("upstream_fixture", helper)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
service, handle = module.build_fixture_service(root)
handle.close()
from twincat_knowledge.corpus.topic_snapshot import activate_snapshot
import hashlib
state = root / "state"
state.mkdir()
manifest_hash = hashlib.sha256((root / "package/manifest.json").read_bytes()).hexdigest()
activate_snapshot(state, root / "package", expected_manifest_sha256=manifest_hash, cache_dir=root / "cache")
config = root / "knowledge.toml"
config.write_text('[paths]\nstate = ' + json.dumps(str(state)) + '\ncache = ' + json.dumps(str(root / "cache")) + '\n', encoding="utf-8")
print(json.dumps({"config": str(config), "fixture": "synthetic-upstream-contract-data"}))
