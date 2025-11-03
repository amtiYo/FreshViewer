.PHONY: help install run test clean build dist

help: ## Show this help message
	@echo "Available commands:"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-20s\033[0m %s\n", $$1, $$2}'

install: ## Install dependencies
	pip install -e .

install-dev: ## Install development dependencies
	pip install -e .
	pip install pytest black flake8 mypy

run: ## Run the application
	python BlurViewer.py

test: ## Run tests
	pytest

lint: ## Run linting
	black BlurViewer.py
	flake8 BlurViewer.py
	mypy BlurViewer.py

format: ## Format code
	black BlurViewer.py

clean: ## Clean build artifacts
	rm -rf build/
	rm -rf dist/
	rm -rf *.egg-info/
	find . -type d -name __pycache__ -delete
	find . -type f -name "*.pyc" -delete

build: ## Build the package
	python -m build

dist: clean ## Create distribution
	python -m build

install-package: ## Install the package in development mode
	pip install -e .

uninstall: ## Uninstall the package
	pip uninstall blurviewer -y
