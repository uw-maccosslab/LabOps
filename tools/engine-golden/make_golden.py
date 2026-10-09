"""Records what LabOps-Projects' Python engine (scripts/project.py) does, as JSON tables that the
C# engine in src/LabOps.Engines is tested against, so the C# tests never need Python.

    uv run --project <LabOps-Projects clone> python tools/engine-golden/make_golden.py <LabOps-Projects clone>

The tables are written to src/LabOps.Tests/Fixtures/engine-golden/. They hold only synthetic
values and the repository's templates, never a real record: the parity tests (ParityTests) compare
the two engines on a real clone at run time instead. LabOps is public and LabOps-Projects is not,
so the templates are read with their comments replaced (their examples name real collaborators),
and no table is written that mentions one of the clone's labs, projects or experiments.

Run it again only to add cases. Once the Python engine is retired, the tables are the record of
how it behaved.
"""
from __future__ import annotations

import datetime as dt
import importlib.util
import json
import random
import re
import sys
from pathlib import Path

OUT = Path(__file__).resolve().parents[2] / "src" / "LabOps.Tests" / "Fixtures" / "engine-golden"


def load_engine(root: Path):
    spec = importlib.util.spec_from_file_location("project_engine", root / "scripts" / "project.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def tag(v):
    """A value with its Python type, since JSON alone cannot tell a date or an int from a string."""
    if v is None:
        return {"t": "null"}
    if isinstance(v, bool):
        return {"t": "bool", "v": v}
    if isinstance(v, int):
        return {"t": "int", "v": str(v)}
    if isinstance(v, float):
        return {"t": "float", "v": repr(v)}
    if isinstance(v, str):
        return {"t": "str", "v": v}
    if isinstance(v, dt.datetime):
        offset = v.utcoffset()
        return {"t": "datetime", "v": v.replace(tzinfo=None).isoformat(),
                "offset": None if offset is None else int(offset.total_seconds())}
    if isinstance(v, dt.date):
        return {"t": "date", "v": v.isoformat()}
    if isinstance(v, list):
        return {"t": "list", "v": [tag(x) for x in v]}
    if isinstance(v, dict):
        return {"t": "dict", "v": [[tag(k), tag(x)] for k, x in v.items()]}
    raise TypeError(f"no tag for {type(v).__name__}")


# ------------------------------------------------------------------ yaml_scalar

CURATED_STRINGS = [
    "", " ", "  ", "plain", "two words", "y", "n", "Y", "N", "yEs", "yes", "Yes", "YES", "no", "No", "NO",
    "true", "True", "TRUE", "false", "False", "FALSE", "on", "On", "ON", "off", "Off", "OFF",
    "null", "Null", "NULL", "~", "nUll", "0o17", "1e3", "1.4.0", "2026-1-5", "2026-10-01 09:30", "2026-10-01",
    "2026-10-01T09:30:00", "2026-10-01 09:30:00", "2026-10-01t09:30:00Z", "20261001", "10:30", "1:30:00", "012",
    "08", "0", "00", "0x1F", "0b101", "1_000", ".5", "1.4", "+1", "-1", "+.inf", ".inf", "-.inf", ".NaN", ".nan",
    "1.0e+3", "1e+3", "a:b", "x#y", "?x", ":x", "a,b", "a, b", "a[b", "a]b", "a{b", "a}b", "end,", "it's",
    'say "hi"', "NIH R01 | supplement", "UPS <dry ice> & cold", "https://panoramaweb.org/x.view?name=a#top",
    "µL", "\U0001F600 emoji", "zero\u200bwidth", "-", "- x", "-x", "?", "=", "<<", "---x", "...x", "---", "...",
    "a: b", "end:", "x: ", "a #b", "a# b", "#comment", ",lead", "[x]", "{x}", "&anchor", "*alias", "!tag", "|pipe",
    ">fold", "'quoted'", '"dq"', "%pct", "@at", "`tick", "it's: x", '"q"', "a\tb", "a\rb", "\abell", "a\bb",
    "\ufeffbom", "\xa0nbsp", "nbsp\xa0", "\u3000ideo", "line1\nline2", "line1\n\nline2", "trailing\n", "\nleading",
    "space \nbreak", "break\n space", "a\x85b", "a\u2028b", "a\u2029b", "\x00nul", "\x1besc", "\x7fdel", "\x80c1",
    "Pat Example, Ph.D.", "92 tubes, 2 hemolyzed: see manifest", "MacCoss-2026-CWZG-MARTEN", "maccoss",
    "/MacCoss/Collaborations/MNRF/BioTRACK", "ELN-4485-20230314-179", "Midwest Neuro Research Foundation (MNRF)",
    "Martes americana", "EDTA plasma", "Homo sapiens", "wait...", "x...", "a  b", "a \tb", "\tlead tab",
    "trail tab\t", "a' b", "'", "''", "\\", "a\\b", "\U0010ffff", "\U0010fffe", "\ud7ff", "\ue000", "\ufffd",
]

ALPHABET = (list("abcxyzAZ019") * 4 + list(" " * 8) + list("_-.,:;#?!&*|>'\"%@`[]{}=~+/\\<") +
            ["\t", "\n", "\r", "\x85", "\xa0", "\u2028", "\u2029", "\ufeff", "\u200b", "\u00e9", "\u00b5",
             "\u4e2d", "\U0001F600", "\x00", "\x1b", "\x7f", "\x80", "\u3000", "---", "..."])


def random_strings(count: int, seed: int) -> list[str]:
    rng = random.Random(seed)
    return ["".join(rng.choices(ALPHABET, k=rng.randint(0, 10))) for _ in range(count)]


def typed_values() -> list:
    big = 123456789012345678901234567890
    utc = dt.timezone.utc
    return [
        None, True, False, 0, 1, -1, 7, 2026, big, -big, 1.5, 1.0, -0.0, 0.1, 1e20, 1e-5, 1e16, 1e15, 123456789.123,
        float("inf"), float("-inf"), float("nan"), 2.5e-10, 3.14159,
        dt.date(2026, 10, 1), dt.date(1, 1, 1), dt.datetime(2026, 10, 1, 9, 30), dt.datetime(2026, 10, 1, 9, 30, 0, 123456),
        dt.datetime(2026, 10, 1, 9, 30, tzinfo=utc),
        dt.datetime(2026, 10, 1, 9, 30, 5, 120, tzinfo=dt.timezone(dt.timedelta(hours=5, minutes=30))),
        dt.datetime(2026, 10, 1, 9, 30, tzinfo=dt.timezone(-dt.timedelta(hours=8))),
        [], {}, [1, "a", None], {"a": 1, "b": [1, {"c": None}]}, {"a": {"b": [1, {"c": None}]}},
        {"id": "samples_received", "kind": "samples_received", "status": "done", "started": dt.date(2026, 9, 9),
         "finished": dt.date(2026, 9, 9), "by": "maccoss", "note": "FedEx overnight on dry ice"},
        {"id": "nta", "kind": "other", "label": "NTA on eluted particles from a reference pool", "status": "pending"},
        {"note": "line1\n\nline2"}, {"note": "it's: here, and [there]"}, ["MacCoss-2026-NWU-SC"],
        {"type": "quote", "quotes": ["MacCoss-2026-CWZG-MARTEN"], "grant": None},
        {"plates": 2, "samples": 160, "imported": dt.date(2026, 10, 3), "covariates": ["Group", "Timepoint"],
         "qc_column": None},
        {"folder": "/MacCoss/Collaborations/MNRF/BioTRACK", "page": "default"},
        {1: "int key", True: "bool key", None: "null key", 1.5: "float key"},
        {"": "empty key"}, {"multi\nline": 1}, {"k" * 130: "long key"}, [[]], [{}], {"a": []}, {"a": {}},
        ["a, b", "c"], {"k": "v", "k2": "a: b"},
    ]


def long_strings() -> list[str]:
    # PyYAML wraps past width=10000; these find out exactly where.
    return ["a " * 6000, "x" * 10050, "it's " * 3000, "\t" + "a b " * 3000, "word " * 2001 + "end",
            "a, b " * 2100, "line\n" * 3 + "z " * 5100]


def scalar_cases(engine) -> list[dict]:
    cases = []
    seen = set()

    def add(v, contexts=("top", "map", "seq", "key")):
        key = repr(v)
        if key in seen:
            return
        seen.add(key)
        case = {"value": tag(v)}
        for context in contexts:
            try:
                if context == "top":
                    case[context] = engine.yaml_scalar(v)
                elif context == "map":
                    case[context] = engine.yaml_scalar({"k": v})
                elif context == "seq":
                    case[context] = engine.yaml_scalar([v])
                elif context == "key":
                    if isinstance(v, (list, dict)):
                        continue
                    case[context] = engine.yaml_scalar({v: 1})
            except Exception as ex:  # recorded, so the C# port knows to refuse it too
                case[context + "_error"] = type(ex).__name__
        cases.append(case)

    for s in CURATED_STRINGS:
        add(s)
    for s in random_strings(3000, seed=20261008):
        add(s)
    for v in typed_values():
        add(v)
    for s in long_strings():
        add(s, contexts=("top", "map", "seq"))
    return cases


# ------------------------------------------------------------------ loading

SCALAR_TOKENS = [
    "yes", "Yes", "YES", "yEs", "no", "No", "NO", "true", "True", "TRUE", "false", "False", "FALSE", "on", "On",
    "ON", "off", "Off", "OFF", "y", "n", "Y", "N", "~", "null", "Null", "NULL", "nUll", "", "0", "00", "012", "08",
    "0o17", "0x1F", "0X1F", "0b101", "1_000", "1:30", "1:30:00", "-1:30", "190:20:30", "+1", "-1", "-0",
    "123456789012345678901234567890", "1.5", ".5", "5.", "1e3", "1.0e+3", "1.0e3", "1.0E+3", "1_0.5", "1:30.5",
    ".inf", "-.Inf", "+.INF", ".NaN", ".nan", ".NAN", "1.4.0", "2026-10-01", "2026-1-5", "2026-01-05",
    "2026-10-01 09:30:00", "2026-10-01T09:30:00Z", "2026-10-01t09:30:00z", "2026-10-01 09:30:00.123456789 +5",
    "2026-10-01T09:30:00-08:00", "2026-10-01 9:30:00", "2026-1-5 9:30:00", "2026-10-01  09:30:00 Z",
    "2026-10-01 09:30", "2026-10-01T09:30:00.5", "20261001", "'2026-10-01'", '"2026-10-01"', "'yes'", '"12"',
    "'null'", "plain text", "a: b", "'a: b'", "x # comment", "x#y", "it's", "'it''s'", '"tab\\there"',
    '"\\u00e9 \\x41 \\U0001F600"', "\"multi\n  line\"", "[a, b, 1]", "{a: 1, b: [x, y]}", "[]", "{}",
    "!!str 123", "!!int '12'", "!!float 1", "!!bool yes", "!!null ''", "!!timestamp 2026-10-01", "! 12",
    "!!str", "&a x", "UPS <dry ice> & cold", "µL", "\U0001F600", "-", "=",
]

DOCUMENTS = [
    "", "\n", "# only a comment\n", "---\n", "--- \n...\n", "k: v", "k: v\n", "\ufeffk: v\n", "- a\n- b\n", "just text\n",
    "12\n", "k: v\n---\nk: w\n", "k: v\nk: w\n", "a: 1\nb: 2\na: 3\n", "1: a\n1.0: b\ntrue: c\n", "yes: x\nno: y\n",
    "~: x\n", "null: x\n", "2026-10-01: x\n", "[a]: 1\n", "{a: 1}: 2\n", "? a\n: b\n", "? [a, b]\n: c\n",
    "base: &b {x: 1, y: 2}\nk:\n  <<: *b\n  y: 3\n", "a: &a {x: 1}\nb: &b {x: 2, z: 9}\nc:\n  <<: [*a, *b]\n  w: 0\n",
    "a: &a 1\nc:\n  <<: *a\n", "a: &x hello\nb: *x\nc: [*x, *x]\n", "k: *nope\n", "k: <<\n", "k: =\n",
    "k: |\n  line1\n  line2\n", "k: |-\n  line1\n", "k: |+\n  line1\n\n", "k: >\n  folded\n  text\n\n  para\n",
    "k: >-\n  a\n  b\n", "k: plain\n  continued\n  more\n", "k: 'single\n  quoted'\n", "k: \"double\n  quoted\"\n",
    "k:\n  - a\n  - b: 1\n    c: 2\n", "k:\n- a\n- b\n", "k: [a, {b: 1}, [c]]\n", "k: {a: [1, 2], b: {c: null}}\n",
    "k: v\n\tbad: tab\n", "k: [a, b\n", "k: {a: 1\n", "k: v\n  bad: indent\n", "k: 'unterminated\n", "a:\nb\n",
    "k: v: w\n", "- a\nk: v\n", "k: v\n- a\n", "k: @x\n", "k: `x\n", "k: !custom x\n", "k: !!python/name:os.system\n",
    "k: !!binary aGVsbG8=\n", "k: !!set {a, b}\n", "k: !!omap [a: 1]\n", "%YAML 1.1\n---\nk: v\n", "k: v # c\nj: w\n",
    "k: \"a\\x41\\u00e9\"\n", "k: 'it''s'\n", "steps:\n  - {id: a, kind: other, label: A}\n  - {id: b, status: done, started: 2026-10-01}\n",
    "steps:\n  - {id: a, started: '2026-10-01'}\n", "steps:\n  - {id: a, started: 2026-02-30}\n", "k: 2026-13-01\n",
    "k: 2026-10-01 25:00:00\n", "k: 0b2\n", "k: 09\n", "k: 1__0\n", "k: 1_\n", "k: _1\n", "k: 0x\n", "k: 1:60\n", "k: 1:5\n",
    "k: -.5\n", "k: +12_345\n", "k: 1:30:00.25\n", "list: [1, 2, 3]\nnested:\n  deeper:\n    deepest: x\n",
    "k: v\r\n", "\"quoted key\": 1\n'single key': 2\n", "k: 'a\n\n  b'\n", "k: \"\\t\"\n", "k: |2\n    indented\n",
]


def templates(root: Path) -> dict[str, str]:
    """The clone's templates by name, each comment's words replaced, since they give real examples.
    Where the comments are is what the edits care about, so that stays."""
    out = {}
    for p in sorted((root / "templates").glob("*.yaml")):
        lines = []
        for line in p.read_text(encoding="utf-8").split("\n"):
            m = re.search(r"(^|\s)#", line)
            if m and "'" not in line[:m.start()] and '"' not in line[:m.start()]:
                line = line[:m.end()] + (" a comment" if line[m.end():].strip() else "")
            lines.append(line)
        out[p.stem] = "\n".join(lines)
    return out


def private_names(root: Path) -> set[str]:
    """The clone's lab, project and experiment folder names, and their longer parts, lowercased."""
    names = set()
    for record in ("*/lab.yaml", "*/*/project.yaml", "*/*/*/experiment.yaml"):
        for folder in (f.parent for f in (root / "projects").glob(record)):
            names.add(folder.name.lower())
            names.update(part.lower() for part in folder.name.split("-") if len(part) >= 4 and not part.isdigit())
    return names - {"maccoss"}


def load_cases(engine, root: Path) -> list[dict]:
    texts = [f"k: {t}\n" for t in SCALAR_TOKENS] + DOCUMENTS
    texts += list(templates(root).values())
    cases = []
    for text in texts:
        case = {"text": text}
        try:
            d, problem = engine.parse_record(text, "x.yaml")
            if problem:
                case["problem"] = problem
            else:
                case["value"] = tag(d)
        except Exception as ex:
            case["error"] = type(ex).__name__
            case["message"] = str(ex)
        cases.append(case)
    return cases


# ------------------------------------------------------------------ text edits

def edit_cases(engine, root: Path) -> dict:
    texts = templates(root)
    project, experiment, lab = texts["project.example"], texts["experiment.example"], texts["lab.example"]
    out = {"set_fields": [], "set_child": [], "write_steps": [], "write_list": [], "set_list": [], "set_wiki": [],
           "block": []}

    def record(name, args, call):
        case = {"args": args}
        try:
            case["result"] = call()
        except Exception as ex:
            case["error"] = type(ex).__name__
            case["message"] = str(ex)
        out[name].append(case)

    simple = "title: Old  # the title\nstatus: active            # active | on_hold | closed\nnotes: []\n"
    block_value = "status: active\nlayout:   # from Octopus\n  plates: 1\n  samples: 80\nsteps: []\n"
    field_sets = [
        ({"status": "on_hold", "series": "dog-aging"}, "status"),
        ({"title": "New title: with colon"}, "status"),
        ({"layout": {"plates": 2, "samples": 160}}, "status"),
        ({"layout": {"plates": 1}}, "expected_samples"),
        ({"project": "X", "title": "T", "human": True}, "status"),
        ({"expected_samples": 73, "species": "Martes americana", "sample_type": "EDTA plasma"}, "human"),
        ({"lab_contact": "maccoss", "series": None}, "status"),
        ({"title": ""}, "status"),
        ({"note": "a #b"}, "status"),
        ({"title": "it's here"}, "nothing"),
        ({"layout": {"plates": 2, "samples": 160, "imported": dt.date(2026, 10, 3), "id_column": "Sample_ID",
                     "covariates": ["Group"], "qc_column": "QC", "subject_column": None, "octopus_version": "1.4.0"}},
         "expected_samples"),
    ]
    raws = [simple, block_value, "project: X\nsteps: []\n", "project: X\nsteps: []\n\n\n", "status: active",
            "title: x\n# comment\nstatus: active\n", "title:\n  - a\n  - b\nstatus: active\n", project, experiment, lab,
            "status: active  #no space\n", "status:\tactive\t# tab comment\n", "status:active\n",
            "title: 'quoted # not a comment'  # real comment\nstatus: active\n"]
    for raw in raws:
        for fields, after in field_sets:
            record("set_fields", {"raw": raw, "fields": tag(fields), "after": after},
                   lambda raw=raw, fields=fields, after=after: engine.set_fields(raw, fields, after))

    funding = ("funding:\n  type: internal          # quote | grant | internal\n  quotes: []              # numbers\n"
               "  grant: null\n\nhuman: false\n")
    for raw in (funding, project, experiment, "funding: {type: quote}\n", "human: false\n",
                "funding:\n  # leading comment\n  type: a#b  # c\n  grant: x\n",
                "funding:\n  type: quote\n# margin comment\n  grant: y\nnext: 1\n"):
        for key, value in (("type", "quote"), ("quotes", ["MacCoss-2026-NWU-SC"]), ("grant", "Midwest Neuro Research Foundation (MNRF)"),
                           ("type", None), ("missing", "added"), ("quotes", [])):
            record("set_child", {"raw": raw, "parent": "funding", "key": key, "value": tag(value)},
                   lambda raw=raw, key=key, value=value: engine.set_child(raw, "funding", key, value))

    steps_raws = [
        "# The timeline.\nsteps:\n  - {id: samples_received, kind: samples_received, status: pending}\n"
        "  - {id: plate_layout, kind: plate_layout, status: pending}\n\nnotes: []\n",
        "steps:\n- id: samples_received\n  kind: samples_received\n  status: in_progress\n- {id: plate_layout, kind: plate_layout}\nnotes: []\n",
        "steps:\n  - {id: a, kind: other, label: A}\n# the rest wait for the second shipment\n  - {id: b, kind: other, label: B}\n\n# Notes about the project.\nnotes: []\n",
        "steps: []\nnotes: []\n", "steps:\n  - {id: a}\n", "notes: []\n", project, experiment,
    ]
    step_lists = [
        [{"id": "samples_received", "kind": "samples_received", "status": "done", "started": dt.date(2026, 10, 1),
          "finished": dt.date(2026, 10, 2), "note": "92 tubes, 2 hemolyzed: see manifest"},
         {"id": "plate_layout", "kind": "plate_layout", "status": "pending"}],
        [{"note": "first", "id": "x", "by": "maccoss", "kind": "other", "label": "Odd: label", "status": "in_progress",
          "assigned": "maccoss", "started": dt.date(2026, 9, 1), "extra": "kept", "_invalid": "dropped", "finished": None,
          "label2": ""}],
        [{"id": "a", "kind": "other"}],
        [],
        [{"id": "a", "kind": "data_acquisition", "status": "done", "started": dt.date(2026, 9, 23),
          "finished": dt.date(2026, 9, 23), "note": "line1\n\nline2"}],
    ]
    for raw in steps_raws:
        for steps in step_lists:
            record("write_steps", {"raw": raw, "steps": tag(steps)},
                   lambda raw=raw, steps=steps: engine.write_steps(raw, [dict(s) for s in steps]))

    list_items = [
        [], [{"folder": "/MacCoss/x/@files/RawFiles", "kind": "raw"}, {"folder": "/MacCoss/y", "kind": "results", "note": None}],
        [{"id": "ELN-4485-20230314-179", "url": "https://panoramaweb.org/MacCoss/x.view?id=1", "title": ""}],
        [{"id": "s-trap-micro-digestion", "version": 3, "title": "S-Trap micro digestion", "step": "sample_prep"}],
    ]
    for raw in (experiment, project, "panorama: []\nnotes: []\n", "panorama:\n  - {folder: /a, kind: raw}\n# c\n\nnotes: []\n",
                "notes: []\n"):
        for key in ("panorama", "notebooks", "protocols"):
            for items in list_items:
                record("write_list", {"raw": raw, "key": key, "items": tag(items)},
                       lambda raw=raw, key=key, items=items: engine.write_list(raw, key, [dict(i) for i in items]))
                record("set_list", {"raw": raw, "key": key, "items": tag(items), "comment": "Protocols used."},
                       lambda raw=raw, key=key, items=items: engine.set_list(raw, key, [dict(i) for i in items], "Protocols used."))

    for raw in (project, "project: X\nwiki: null\nnotes: []\n", "project: X\nwiki:\n  folder: /a\n  page: b\n# c\nnotes: []\n",
                "project: X\n", "project: X\n\n\n"):
        for value in (None, {"folder": "/MacCoss/Collaborations/MNRF/BioTRACK", "page": "default"}):
            record("set_wiki", {"raw": raw, "value": tag(value)}, lambda raw=raw, value=value: engine.set_wiki(raw, value))

    for raw in [*raws, *steps_raws, funding]:
        lines = raw.split("\n")
        for key in ("status", "steps", "notes", "title", "layout", "funding", "project", "missing", "panorama"):
            where = engine._block(lines, key)
            out["block"].append({"raw": raw, "key": key, "result": list(where) if where else None})
    return out


JSON_TEXTS = [
    "", " ", "1", "-0", "1.5", "1e5", "1E+5", "-1.25e-3", "01", "1.", ".5", "-", "1e", "1e+", "123456789012345678901234567890",
    "true", "false", "null", "tru", "nul", "NaN", "Infinity", "-Infinity", "-Inf", "[NaN, Infinity, -Infinity]",
    '"a"', '"a', '"a\\"b"', '"\\u00e9\\ud83d\\ude00"', '"\\ud83d"', '"\\ud83dx"', '"\\u12"', '"\\u12', '"\\x"', '"tab\there"',
    '"\\/\\b\\f\\n\\r\\t"', "[]", "[1, 2]", "[1, 2,]", "[1 2]", "[", "[1,", "]", "{}", '{"a": 1}', '{"a": 1,}', '{"a" 1}',
    '{"a": }', "{a: 1}", "{", '{"a"', '{"a":', '{"a": [1, 2}', '{"a": 1, "a": 2, "b": 3}', '{"b": 1, "a": {"c": [true, null]}}',
    "[1] x", "[1]  \n  ", "\n\n  [1,\n 2,\n x]", '﻿[1]', '["中\U0001F600", 1]', '\U0001F600[', "  {\"k\": \"v\"}  ",
    '[1, {"a": "b\nc"}]', "[-]", "[0.e1]", "[1e1.5]", "[- 1]",
]


def json_cases() -> list[dict]:
    cases = []
    for text in JSON_TEXTS:
        case = {"text": text}
        try:
            case["value"] = tag(json.loads(text))
        except json.JSONDecodeError as ex:
            case["error"] = str(ex)
        cases.append(case)
    return cases


EXTRA_HEADERS = [
    "", "Name", "names", "Patient_Name", "PatientName", "patientNAME", "Ｎａｍｅ", "Na​me", "Prénom", "Apellido",
    "Address1", "HomeAddress2", "e_mail", "E Mail", "Telephone #", "Zip+4", "ZIP_CODE", "PostCode", "Social Security",
    "SocialSecurityNumber", "MedicalRecordNo", "Subject Initial", "Middle Initial", "Initial", "Initial weight",
    "Pt Initial", "Specificity", "mRNA", "Cities", "Counties", "Addresses", "Emails", "Phones", "Owners", "Contacts",
    "Sample_Name", "SampleName", "filename", "Run name", "raw file name", "Lab Name", "Folder name", "dataset name",
    "Patient ID", "patient", "Patient #", "Donor", "Batch3", "Plate10", "Ａｄｄｒｅｓｓ", "naïve", "Sample­Name",
    "名前", "Tube 1", "x" * 40, "First-Name", "first.name", "FIRST NAME", "FullName", "Birth Year", "BirthWeight",
    "Collection date", "Age", "Sex", "Notes", "Comments",
]

CONTACT_TEXTS = [
    "", "206-555-0100", "(206) 555-0100", "+1 206 555 0100", "206.555.0100", "2065550100", "+44 20 7946 0958",
    "+57.021464", "+57.02 1464", "pat@example.org", "pat at example dot org", "a@b.c", "x@y.co", "S-123-4567-8",
    "123-456-7890 and more", "call 555 0100", "+1 (206) 555-0100", "+١٢٣ 4567 8901", "BC-2026-0001",
    "1-800-555-0199", "+33 1 23 45 67 89", "(206)555-0100", "206 555 0100", "206-5550100",
]


def deidentification_cases(engine, root: Path) -> dict:
    sys.path.insert(0, str(root / "tests"))
    spec = importlib.util.spec_from_file_location("test_deid", root / "tests" / "test_deidentification.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    headers = list(EXTRA_HEADERS) + list(getattr(module, "LAB_TEMPLATE", []))
    for item in vars(module).values():
        for mark in getattr(item, "pytestmark", []):
            if mark.name == "parametrize" and mark.args[0] == "header":
                headers += [h for h in mark.args[1] if isinstance(h, str)]
    seen, header_cases = set(), []
    for h in headers:
        if h in seen:
            continue
        seen.add(h)
        hit = engine.check_header(h)
        header_cases.append({"header": h, "words": engine.header_words(h), "hit": list(hit) if hit else None})
    contact_cases = [{"text": t, "phone": engine.has_phone(t), "contact": engine.has_contact_details(t)} for t in CONTACT_TEXTS]
    return {"headers": header_cases, "contacts": contact_cases}


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    root = Path(sys.argv[1]).resolve()
    engine = load_engine(root)
    OUT.mkdir(parents=True, exist_ok=True)
    tables = {
        "yaml-scalar.json": {"engine_version": engine.ENGINE_VERSION, "cases": scalar_cases(engine)},
        "yaml-load.json": {"engine_version": engine.ENGINE_VERSION, "cases": load_cases(engine, root)},
        "yaml-edits.json": {"engine_version": engine.ENGINE_VERSION, **edit_cases(engine, root)},
        "json-load.json": {"engine_version": engine.ENGINE_VERSION, "cases": json_cases()},
        "deidentification.json": {"engine_version": engine.ENGINE_VERSION, **deidentification_cases(engine, root)},
    }
    texts = {name: json.dumps(table, indent=1, ensure_ascii=True) + "\n" for name, table in tables.items()}
    leaks = sorted({(name, n) for name, text in texts.items() for n in private_names(root) if n in text.lower()})
    if leaks:
        for name, n in leaks:
            print(f"{name} mentions {n!r}, a name from the clone's projects; LabOps is public.", file=sys.stderr)
        print("Nothing was written. Replace the name where the case comes from and run this again.", file=sys.stderr)
        return 1
    for name, text in texts.items():
        (OUT / name).write_text(text, encoding="utf-8", newline="\n")
        print(f"{name}: written")
    return 0


if __name__ == "__main__":
    sys.exit(main())
