# Contributing to BlurViewer

Thank you for your interest in contributing to BlurViewer! This document provides guidelines and information for contributors.

## 🚀 Quick Start

1. **Fork** the repository
2. **Clone** your fork: `git clone https://github.com/yourusername/BlurViewer.git`
3. **Create** a feature branch: `git checkout -b feature/amazing-feature`
4. **Make** your changes and test thoroughly
5. **Commit** with clear messages: `git commit -m 'Add amazing feature'`
6. **Push** to your branch: `git push origin feature/amazing-feature`
7. **Create** a Pull Request with detailed description

## 🛠️ Development Setup

### Prerequisites
- Python 3.8 or higher
- Git

### Installation
```bash
# Clone the repository
git clone https://github.com/amtiYo/BlurViewer.git
cd BlurViewer

# Create virtual environment
python -m venv .venv
source .venv/bin/activate  # On Windows: .venv\Scripts\activate

# Install dependencies
pip install -e .

# Run the application
python BlurViewer.py
```

### Using Makefile (optional)
```bash
make install-package  # Install package in development mode
make run              # Run the application
make format           # Format code
make lint             # Run linting
```

## 📝 Code Style

- Follow PEP 8 style guidelines
- Use meaningful variable and function names
- Add comments for complex logic
- Keep functions small and focused
- Write docstrings for public functions

### Code Formatting
```bash
# Format code with black
black BlurViewer.py

# Check code style with flake8
flake8 BlurViewer.py
```

## 🧪 Testing

Before submitting a pull request, please ensure:

1. **Code runs without errors** - Test the application thoroughly
2. **No new warnings** - Fix any linting issues
3. **Backward compatibility** - Don't break existing functionality
4. **Cross-platform compatibility** - Test on different operating systems if possible

## 📋 Pull Request Guidelines

### Before submitting a PR:

1. **Update documentation** if needed
2. **Add tests** for new features
3. **Update requirements.txt** if adding new dependencies
4. **Test on different image formats** if making changes to image processing
5. **Check performance** - ensure no significant performance regressions

### PR Description should include:

- **Summary** of changes
- **Motivation** for the change
- **Testing** performed
- **Screenshots** if UI changes
- **Breaking changes** if any

## 🐛 Bug Reports

When reporting bugs, please include:

- **Operating system** and version
- **Python version**
- **Steps to reproduce**
- **Expected behavior**
- **Actual behavior**
- **Screenshots** if applicable
- **Error messages** if any

## 💡 Feature Requests

When suggesting features:

- **Describe the feature** clearly
- **Explain the use case**
- **Provide examples** if possible
- **Consider implementation complexity**

## 📄 License

By contributing to BlurViewer, you agree that your contributions will be licensed under the MIT License.

## 🤝 Questions?

If you have questions about contributing, feel free to:

- Open an issue for discussion
- Ask in the discussions section
- Contact the maintainer directly

Thank you for contributing to BlurViewer! 🎉
