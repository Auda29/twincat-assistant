import React from 'react';

interface ProductLogoProps {
  className?: string;
  width?: number;
  height?: number;
  isAnimated?: boolean;
  variant?: 'default' | 'splash';
}

// Keep the component export contract for existing layout consumers. The mark
// belongs to TwinCAT Assistant, independent of the upstream engine and renderer.
export const OpenChamberLogo: React.FC<ProductLogoProps> = ({ className, width = 70, height = 70, variant = 'default' }) => (
  <svg className={className} width={width} height={height} viewBox="0 0 64 64" role="img" aria-label="TwinCAT Assistant"
    style={{ color: variant === 'splash' ? 'var(--splash-stroke, var(--surface-foreground))' : 'var(--surface-foreground)' }}
    fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
    <rect x="5" y="5" width="54" height="54" rx="12" />
    <path d="M14 21h18M23 21v22M49 23a11 11 0 1 0 0 18" />
  </svg>
);
