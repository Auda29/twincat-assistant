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
export const OpenChamberLogo: React.FC<ProductLogoProps> = ({ className, width = 70, height = 70 }) => (
  <svg className={className} width={width} height={height} viewBox="0 0 64 64" role="img" aria-label="TwinCAT Assistant">
    <rect x="5" y="6" width="54" height="44" rx="12" fill="#1d5fc2" />
    <path d="M14 46v13l20-12" fill="#1d5fc2" />
    <path d="m23 21-8 8 8 8m18-16 8 8-8 8" fill="none" stroke="#fff" strokeWidth="3.5" strokeLinecap="round" strokeLinejoin="round" />
    <path d="m35 20-6 18" fill="none" stroke="#66e3d3" strokeWidth="3.5" strokeLinecap="round" />
  </svg>
);
