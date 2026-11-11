"""Actual pinned dependencies, private validator calls only; no operator corpus."""
import importlib.util
import json
import pathlib
import sys
import time

root = pathlib.Path(sys.argv[1])
sys.path.insert(0, str(root / "site-packages"))
from twincat_knowledge.domain import provenance as patched

spec = importlib.util.spec_from_file_location(
    "twincat_knowledge.domain.original_provenance", root / "assistant-overlay/original-provenance.py"
)
original = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = original
spec.loader.exec_module(original)
values = ["topic/a.html#heading", "topic/%C3%A4.html", "文档/a.html", "a" * 2049,
          "", "../a", "/root", "C:/file", "file:stream", "aux.txt", "%252e%252e/a", "x\\y"]
for control in [chr(i) for i in range(32)] + [chr(127)]:
    values.extend(["a" + control + "b", "a%" + format(ord(control), "02x") + "b",
                   "a%25" + format(ord(control), "02x") + "b"])
def outcome(function, value):
    try:
        return ("ok", function(value))
    except Exception as error:
        return (type(error).__name__, str(error))
cases = 0
for value in values:
    for name in ["_validate_locator", "_validate_canonical_url"]:
        assert outcome(getattr(original, name), value) == outcome(getattr(patched, name), value)
        cases += 1
patched._validate_locator.cache_clear()
for _ in range(2):
    outcome(patched._validate_locator, "../unsafe")
assert patched._validate_locator.cache_info().currsize == 0
locator = "topic/" + "station-" * 100 + ".html#section"
times = {}
for name, module in [("original", original), ("patched", patched)]:
    start = time.perf_counter()
    for _ in range(10000):
        module._validate_locator(locator)
    times[name] = round((time.perf_counter() - start) * 1000, 2)
assert patched._validate_locator.cache_info().hits >= 9999
for i in range(5000):
    patched._validate_locator("topic/" + str(i) + ".html")
assert patched._validate_locator.cache_info().currsize <= 4096
print(json.dumps(dict(equivalent=True, cases=cases, maxsize=4096, cachedErrors=0,
                      syntheticRepeatedLocatorMs=times)))
