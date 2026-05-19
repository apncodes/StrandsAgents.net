import React from 'react';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';

export default function Home(): React.JSX.Element {
  const { siteConfig } = useDocusaurusContext();
  return (
    <Layout
      title={siteConfig.title}
      description={siteConfig.tagline}>
      <main style={{ padding: '4rem 2rem', textAlign: 'center' }}>
        <h1>{siteConfig.title}</h1>
        <p style={{ fontSize: '1.25rem', marginBottom: '2rem' }}>{siteConfig.tagline}</p>
        <Link
          className="button button--primary button--lg"
          to="/docs/intro">
          Get Started
        </Link>
      </main>
    </Layout>
  );
}
