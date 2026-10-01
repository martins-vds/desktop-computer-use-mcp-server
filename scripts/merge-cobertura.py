#!/usr/bin/env python3
"""Union line hits across test assemblies before CRAP analysis."""

import argparse
import copy
from pathlib import Path
import xml.etree.ElementTree as ET


def source_path(report, filename):
    path = Path(filename)
    if path.is_absolute():
        return str(path.resolve())
    for source in report.findall("sources/source"):
        candidate = Path(source.text or ".") / path
        if candidate.is_file():
            return str(candidate.resolve())
    sources = report.findall("sources/source")
    if sources:
        # Compiler-generated source names need not exist on disk.
        return str((Path(sources[0].text or ".") / path).resolve())
    raise ValueError(f"Coverage report has no source root for: {filename}")


def merge_lines(target, incoming):
    lines = {line.get("number"): line for line in target.findall("line")}
    for line in incoming.findall("line"):
        number = line.get("number")
        if number not in lines:
            cloned = copy.deepcopy(line)
            target.append(cloned)
            lines[number] = cloned
        else:
            existing = lines[number]
            existing.set("hits", str(int(existing.get("hits", "0")) + int(line.get("hits", "0"))))
            # Cobertura does not identify which side of a jump was covered.
            # Preserve the strongest observed branch lower bound, not a fabricated union.
            if branch_fraction(line) > branch_fraction(existing):
                existing.set("condition-coverage", line.get("condition-coverage", "0% (0/0)"))
                old = existing.find("conditions")
                if old is not None:
                    existing.remove(old)
                conditions = line.find("conditions")
                if conditions is not None:
                    existing.append(copy.deepcopy(conditions))
    target[:] = sorted(target, key=lambda line: int(line.get("number", "0")))


def branch_fraction(line):
    coverage = line.get("condition-coverage", "0%")
    return float(coverage.split("%", 1)[0]) / 100


def line_rate(lines):
    items = lines.findall("line")
    covered = sum(int(line.get("hits", "0")) > 0 for line in items)
    return str(covered / len(items) if items else 0)


def merge_reports(inputs, output):
    classes = {}
    for filename in inputs:
        report = ET.parse(filename).getroot()
        for incoming in report.findall(".//class"):
            key = (incoming.get("name"), source_path(report, incoming.get("filename")))
            if key not in classes:
                classes[key] = copy.deepcopy(incoming)
                classes[key].set("filename", key[1])
                continue
            target = classes[key]
            target.set("branch-rate", str(max(
                float(target.get("branch-rate", "0")), float(incoming.get("branch-rate", "0")))))
            merge_lines(target.find("lines"), incoming.find("lines"))
            methods = target.find("methods")
            lookup = {(method.get("name"), method.get("signature")): method for method in methods}
            for method in incoming.findall("methods/method"):
                identity = (method.get("name"), method.get("signature"))
                if identity in lookup:
                    existing = lookup[identity]
                    merge_lines(existing.find("lines"), method.find("lines"))
                    existing.set("branch-rate", str(max(
                        float(existing.get("branch-rate", "0")), float(method.get("branch-rate", "0")))))
                else:
                    cloned = copy.deepcopy(method)
                    methods.append(cloned)
                    lookup[identity] = cloned

    root = ET.Element("coverage", {"version": "merged", "line-rate": "0"})
    ET.SubElement(ET.SubElement(root, "sources"), "source").text = "/"
    package = ET.SubElement(ET.SubElement(root, "packages"), "package", {"name": "merged"})
    destination = ET.SubElement(package, "classes")
    for key in sorted(classes):
        item = classes[key]
        item.set("line-rate", line_rate(item.find("lines")))
        for method in item.findall("methods/method"):
            method.set("line-rate", line_rate(method.find("lines")))
        destination.append(item)
    unique_lines = {}
    for item in destination:
        for line in item.findall("lines/line"):
            identity = (item.get("filename"), line.get("number"))
            unique_lines[identity] = unique_lines.get(identity, 0) + int(line.get("hits", "0"))
    valid = len(unique_lines)
    covered = sum(hits > 0 for hits in unique_lines.values())
    root.set("lines-valid", str(valid))
    root.set("lines-covered", str(covered))
    root.set("line-rate", str(covered / valid if valid else 0))
    output.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(root).write(output, encoding="utf-8", xml_declaration=True)
    print(f"Merged {len(inputs)} reports: {covered}/{valid} unique source lines covered.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    reports = sorted(path for path in args.directory.rglob("coverage.cobertura.xml")
                     if path.resolve() != args.output.resolve())
    if not reports:
        raise ValueError(f"No Cobertura reports found in {args.directory}")
    merge_reports(reports, args.output)
