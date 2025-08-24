import { useState, useEffect } from 'react';

export function useTheme() {
  const [theme, setTheme] = useState(() => {
    // Get theme from localStorage or default to 'system'
    const savedTheme = localStorage.getItem('appTheme');
    return savedTheme || 'system';
  });

  const [resolvedTheme, setResolvedTheme] = useState('light');

  // Function to get system theme
  const getSystemTheme = () => {
    if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
      return 'dark';
    }
    return 'light';
  };

  // Function to apply theme to document
  const applyTheme = (newTheme) => {
    const root = document.documentElement;
    
    if (newTheme === 'system') {
      const systemTheme = getSystemTheme();
      setResolvedTheme(systemTheme);
    } else {
      setResolvedTheme(newTheme);
    }
    
    // Set the class for Tailwind dark mode
    if (newTheme === 'dark' || (newTheme === 'system' && getSystemTheme() === 'dark')) {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }
  };

  // Function to change theme
  const changeTheme = (newTheme) => {
    setTheme(newTheme);
    localStorage.setItem('appTheme', newTheme);
    applyTheme(newTheme);
  };

  // Effect to handle theme changes
  useEffect(() => {
    applyTheme(theme);

    // Listen for system theme changes
    if (theme === 'system') {
      const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
      const handleChange = () => {
        applyTheme('system');
      };

      mediaQuery.addEventListener('change', handleChange);
      return () => mediaQuery.removeEventListener('change', handleChange);
    }
  }, [theme]);

  return {
    theme,
    resolvedTheme,
    changeTheme,
    isDark: resolvedTheme === 'dark',
    isLight: resolvedTheme === 'light',
  };
}
