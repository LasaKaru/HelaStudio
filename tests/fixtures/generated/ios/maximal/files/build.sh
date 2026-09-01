#!/bin/sh
# Builds this project on a Mac with nothing but Xcode and Homebrew installed.
#
# This is the beginning of the source-export feature (BD-10): a customer who
# leaves should be able to build what they were paying for. It costs almost
# nothing to emit now and would be much harder to retrofit later.
set -eu

if ! command -v xcodegen >/dev/null 2>&1; then
	echo "XcodeGen is required: brew install xcodegen" >&2
	exit 1
fi

# The Xcode project is generated from project.yml rather than committed. A
# .pbxproj is a merge-conflict generator, and regenerating is instant.
xcodegen generate

xcodebuild build \
	-project "acme_orders.xcodeproj" \
	-scheme "acme_orders" \
	-destination 'generic/platform=iOS' \
	CODE_SIGNING_ALLOWED=NO
