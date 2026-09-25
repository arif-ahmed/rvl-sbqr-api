#!/usr/bin/env python3
"""Convert a BQR Secure Manager OpenAPI document into a Postman v2.1 collection
plus dev/stage/production environment files.

Usage:
  python generate_postman.py --scope public --openapi https://localhost:5080/openapi/v1.public.json --out-dir .postman
  python generate_postman.py --scope internal --openapi ./v1.internal-admin.json --out-dir .postman

No third-party dependencies (stdlib only).
"""
import argparse
import json
import sys
import urllib.request
import base64
import re
from pathlib import Path
from urllib.parse import urlparse

SCOPE_META = {
    "public": {
        "title": "BQR Secure Manager - Public API",
        "doc_name": "v1.public",
    },
    "internal": {
        "title": "BQR Secure Manager - Internal Admin API",
        "doc_name": "v1.internal-admin",
    },
}

PLACEHOLDER_BY_TYPE_FORMAT = {
    ("string", "uuid"): "00000000-0000-0000-0000-000000000000",
    ("string", "date-time"): "2026-01-01T00:00:00Z",
    ("string", "date"): "2026-01-01",
    ("string", "email"): "user@example.com",
    ("string", "byte"): "base64string",
}

# OAuth-specific field placeholders
OAUTH_FIELD_PLACEHOLDERS = {
    "grant_type": "client_credentials",
    "client_id": "your-client-id",
    "client_secret": "your-client-secret",
    "scope": "admin",
}


def load_openapi(source: str, basic_user: str | None, basic_pass: str | None) -> dict:
    parsed = urlparse(source)
    if parsed.scheme in ("http", "https"):
        req = urllib.request.Request(source)
        if basic_user is not None:
            token = base64.b64encode(f"{basic_user}:{basic_pass or ''}".encode()).decode()
            req.add_header("Authorization", f"Basic {token}")
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read().decode())
    return json.loads(Path(source).read_text(encoding="utf-8"))


def resolve_ref(spec: dict, ref: str) -> dict:
    # Only local component refs are supported: "#/components/schemas/Foo"
    parts = ref.lstrip("#/").split("/")
    node = spec
    for part in parts:
        node = node[part]
    return node


def example_for_schema(spec: dict, schema: dict, depth: int = 0, field_name: str = "") -> object:
    if depth > 6 or schema is None:
        return None
    if "$ref" in schema:
        return example_for_schema(spec, resolve_ref(spec, schema["$ref"]), depth + 1, field_name)
    if "example" in schema:
        return schema["example"]
    if "default" in schema:
        return schema["default"]
    if "enum" in schema and schema["enum"]:
        return schema["enum"][0]

    schema_type = schema.get("type")
    if schema_type == "object" or "properties" in schema:
        props = schema.get("properties", {})
        if not props:
            # e.g. {"type": "object", "oneOf": [{"$ref": ...}], "nullable": true} -
            # the shape Swashbuckle emits for a nullable request DTO. Resolve the
            # combinator instead of returning an empty object.
            for combinator in ("oneOf", "anyOf", "allOf"):
                if combinator in schema and schema[combinator]:
                    return example_for_schema(spec, schema[combinator][0], depth + 1, field_name)
        required = set(schema.get("required", []))
        result = {}
        for name, prop_schema in props.items():
            # Always include required fields; include a couple of optional ones for shape context.
            if required and name not in required:
                continue
            result[name] = example_for_schema(spec, prop_schema, depth + 1, name)
        if not result and props:
            # No required fields declared: include everything so the body isn't empty.
            for name, prop_schema in props.items():
                result[name] = example_for_schema(spec, prop_schema, depth + 1, name)
        return result
    if schema_type == "array":
        item_schema = schema.get("items", {})
        return [example_for_schema(spec, item_schema, depth + 1, field_name)]
    if schema_type == "integer":
        return 0
    if schema_type == "number":
        return 0.0
    if schema_type == "boolean":
        return True
    if schema_type == "string":
        # Check for OAuth-specific fields first
        if field_name in OAUTH_FIELD_PLACEHOLDERS:
            return OAUTH_FIELD_PLACEHOLDERS[field_name]
        key = ("string", schema.get("format"))
        return PLACEHOLDER_BY_TYPE_FORMAT.get(key, "string")
    # oneOf/anyOf/allOf fallback: use the first branch
    for combinator in ("oneOf", "anyOf", "allOf"):
        if combinator in schema and schema[combinator]:
            return example_for_schema(spec, schema[combinator][0], depth + 1, field_name)
    return None


def folder_name_for(path: str, operation: dict) -> str:
    tags = operation.get("tags")
    if tags:
        return tags[0]
    # Filter out version segments (both parameterized and hardcoded like v1, v2, etc.)
    segments = [
        s for s in path.split("/")
        if s and not s.startswith("{") and s != "v{version:apiVersion}" and not re.match(r'^v\d+$', s)
    ]
    return segments[0] if segments else "root"


def build_url(path: str) -> tuple[str, list[dict]]:
    """Convert OpenAPI {param} path segments to Postman :param segments.
    Replace both parameterized (v{version:apiVersion}) and hardcoded (v1, v2, etc.)
    version segments with Postman's {{apiVersion}} variable.
    """
    postman_path = path.replace("v{version:apiVersion}", "{{apiVersion}}")
    # Also replace hardcoded version numbers like /v1/, /v2/, etc. with /{{apiVersion}}/
    postman_path = re.sub(r'/v\d+/', '/{{apiVersion}}/', postman_path)

    path_vars = []
    parts = []
    for segment in postman_path.split("/"):
        # Skip already-templated Postman variables (e.g. "{{apiVersion}}") -
        # only single-brace OpenAPI path params (e.g. "{id}") become :id.
        if segment.startswith("{") and segment.endswith("}") and not segment.startswith("{{"):
            name = segment[1:-1]
            parts.append(f":{name}")
            path_vars.append({"key": name, "value": ""})
        else:
            parts.append(segment)
    return "/".join(parts), path_vars


def build_request_item(spec: dict, path: str, method: str, operation: dict) -> dict:
    raw_path, path_vars = build_url(path)
    raw_url = "{{baseUrl}}/" + raw_path.lstrip("/")

    query = []
    headers = []
    for param in operation.get("parameters", []):
        if "$ref" in param:
            param = resolve_ref(spec, param["$ref"])
        location = param.get("in")
        param_name = param.get("name", "")
        entry = {
            "key": param_name,
            "value": str(example_for_schema(spec, param.get("schema", {}), field_name=param_name) or ""),
            "description": param.get("description", ""),
            "disabled": not param.get("required", False),
        }
        if location == "query":
            query.append(entry)
        elif location == "header":
            headers.append(entry)

    url = {"raw": raw_url + ("?" + "&".join(f"{q['key']}={q['value']}" for q in query) if query else ""),
           "host": ["{{baseUrl}}"], "path": [p for p in raw_path.split("/") if p]}
    if query:
        url["query"] = query
    if path_vars:
        url["variable"] = path_vars

    request: dict = {
        "method": method.upper(),
        "header": headers,
        "url": url,
    }

    request_body = operation.get("requestBody")
    if request_body:
        content = request_body.get("content", {})
        json_content = content.get("application/json") or content.get("application/*+json")
        if json_content:
            body_example = example_for_schema(spec, json_content.get("schema", {}))
            request["header"].append({"key": "Content-Type", "value": "application/json"})
            request["body"] = {
                "mode": "raw",
                "raw": json.dumps(body_example, indent=2),
                "options": {"raw": {"language": "json"}},
            }
        elif "application/x-www-form-urlencoded" in content:
            form_schema = content["application/x-www-form-urlencoded"].get("schema", {})
            example = example_for_schema(spec, form_schema) or {}
            request["body"] = {
                "mode": "urlencoded",
                "urlencoded": [{"key": k, "value": str(v)} for k, v in example.items()],
            }

    # Special case: if this is an /oauth/token POST and no body was generated from OpenAPI,
    # generate the OAuth form fields manually (the spec may not document them properly).
    is_token_endpoint = path.endswith("/oauth/token")
    if is_token_endpoint and method.upper() == "POST" and "body" not in request:
        request["body"] = {
            "mode": "urlencoded",
            "urlencoded": [
                {"key": "grant_type", "value": OAUTH_FIELD_PLACEHOLDERS.get("grant_type", "client_credentials")},
                {"key": "client_id", "value": OAUTH_FIELD_PLACEHOLDERS.get("client_id", "your-client-id")},
                {"key": "client_secret", "value": OAUTH_FIELD_PLACEHOLDERS.get("client_secret", "your-client-secret")},
            ],
        }

    item = {
        "name": operation.get("summary") or f"{method.upper()} {path}",
        "request": request,
        "response": [],
    }
    if is_token_endpoint:
        item["request"]["auth"] = {"type": "noauth"}
        item["event"] = [{
            "listen": "test",
            "script": {
                "type": "text/javascript",
                "exec": [
                    "pm.test('Token request succeeded', function () {",
                    "    pm.response.to.have.status(200);",
                    "});",
                    "if (pm.response.code === 200) {",
                    "    const body = pm.response.json();",
                    "    if (body.accessToken) {",
                    "        pm.environment.set('accessToken', body.accessToken);",
                    "        console.log('accessToken captured into environment');",
                    "    }",
                    "}",
                ],
            },
        }]

    return item


def build_oauth_token_request() -> dict:
    """Synthesize a POST /v{version}/oauth/token request.

    The public OpenAPI document already exposes this endpoint, so when generating
    from that doc the request will be produced normally. The internal-admin doc
    deliberately omits it (the OAuth controller has no `internal` GroupName), yet
    internal users still need to mint a bearer token before calling the admin
    surface. Injecting the request here means both collections ship with the
    same auth entry point and the auto-capture test script works identically.
    """
    raw_path = "{{apiVersion}}/oauth/token"
    return {
        "name": "POST /{{apiVersion}}/oauth/token",
        "request": {
            "method": "POST",
            "header": [],
            "url": {
                "raw": "{{baseUrl}}/" + raw_path,
                "host": ["{{baseUrl}}"],
                "path": raw_path.split("/"),
            },
            "body": {
                "mode": "urlencoded",
                "urlencoded": [
                    {"key": "grant_type", "value": OAUTH_FIELD_PLACEHOLDERS["grant_type"]},
                    {"key": "client_id", "value": OAUTH_FIELD_PLACEHOLDERS["client_id"]},
                    {"key": "client_secret", "value": OAUTH_FIELD_PLACEHOLDERS["client_secret"]},
                ],
            },
            "auth": {"type": "noauth"},
        },
        "response": [],
        "event": [{
            "listen": "test",
            "script": {
                "type": "text/javascript",
                "exec": [
                    "pm.test('Token request succeeded', function () {",
                    "    pm.response.to.have.status(200);",
                    "});",
                    "if (pm.response.code === 200) {",
                    "    const body = pm.response.json();",
                    "    if (body.accessToken) {",
                    "        pm.environment.set('accessToken', body.accessToken);",
                    "        console.log('accessToken captured into environment');",
                    "    }",
                    "}",
                ],
            },
        }],
    }


def build_collection(spec: dict, scope: str) -> dict:
    meta = SCOPE_META[scope]
    folders: dict[str, list[dict]] = {}
    for path, methods in spec.get("paths", {}).items():
        for method, operation in methods.items():
            if method.lower() not in ("get", "post", "put", "patch", "delete"):
                continue
            folder = folder_name_for(path, operation)
            folders.setdefault(folder, []).append(build_request_item(spec, path, method, operation))

    # Always ensure the OAuth token endpoint is present in every generated
    # collection — internal users still need to call it to mint a bearer
    # before hitting the admin surface, and the public doc's version of the
    # request already drives the same auto-capture script.
    oauth_token_request = build_oauth_token_request()
    oauth_already_present = any(
        req.get("name") == oauth_token_request["name"]
        for folder_items in folders.values()
        for req in folder_items
    )
    if not oauth_already_present:
        # Remove any partial OAuth folder entries the OpenAPI doc happened to
        # include without the token endpoint, then add our synthesised one.
        folders.pop("OAuth", None)
        folders["OAuth"] = [oauth_token_request]

    items = [{"name": name, "item": reqs} for name, reqs in sorted(folders.items())]

    return {
        "info": {
            "name": meta["title"],
            "description": f"Auto-generated from {meta['doc_name']} OpenAPI document. "
                            f"Scope: {scope} only — the other surface is intentionally excluded. "
                            f"The OAuth token endpoint is included in every collection so any "
                            f"caller can mint a bearer before invoking scoped operations.",
            "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
        },
        "auth": {
            "type": "bearer",
            "bearer": [{"key": "token", "value": "{{accessToken}}", "type": "string"}],
        },
        "variable": [
            {"key": "apiVersion", "value": "v1"},
        ],
        "item": items,
    }


def build_environment(name: str, base_url: str) -> dict:
    return {
        "name": name,
        "values": [
            {"key": "baseUrl", "value": base_url, "type": "default", "enabled": True},
            {"key": "clientId", "value": "", "type": "secret", "enabled": True},
            {"key": "clientSecret", "value": "", "type": "secret", "enabled": True},
            {"key": "accessToken", "value": "", "type": "secret", "enabled": True},
        ],
        "_postman_variable_scope": "environment",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scope", choices=["public", "internal"], required=True)
    parser.add_argument("--openapi", required=True, help="URL or local path to the OpenAPI JSON document")
    parser.add_argument("--basic-user", default=None, help="Basic auth username for fetching the internal-admin doc")
    parser.add_argument("--basic-pass", default=None, help="Basic auth password for fetching the internal-admin doc")
    parser.add_argument("--out-dir", default=".postman")
    parser.add_argument("--local-base-url", default="http://localhost:5001")
    parser.add_argument("--dev-base-url", default="http://localhost:5080")
    parser.add_argument("--stage-base-url", default="https://stage.bqr.internal")
    parser.add_argument("--prod-base-url", default="https://api.bqr.example.com")
    args = parser.parse_args()

    spec = load_openapi(args.openapi, args.basic_user, args.basic_pass)
    collection = build_collection(spec, args.scope)

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    collection_path = out_dir / f"BQR-{args.scope}.postman_collection.json"
    collection_path.write_text(json.dumps(collection, indent=2), encoding="utf-8")

    envs = {
        "Local": args.local_base_url,
        "Dev": args.dev_base_url,
        "Stage": args.stage_base_url,
        "Production": args.prod_base_url,
    }
    for env_name, base_url in envs.items():
        env = build_environment(f"BQR Secure Manager - {env_name}", base_url)
        env_path = out_dir / f"BQR-{env_name}.postman_environment.json"
        env_path.write_text(json.dumps(env, indent=2), encoding="utf-8")

    operation_count = sum(len(v["item"]) for v in collection["item"])
    print(f"Wrote {collection_path} ({operation_count} requests across {len(collection['item'])} folders)")
    for env_name in envs:
        print(f"Wrote {out_dir / f'BQR-{env_name}.postman_environment.json'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
